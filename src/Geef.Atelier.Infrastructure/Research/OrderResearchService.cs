using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Research;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Persistence.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>
/// The research of an order (contract stage 4, 4.1–4.8): selects the topics, searches, lets the model pick passages, keeps only those
/// found word for word and stores the result once per order. Every failure ends as a status with <see cref="OrderResearch.ErrorCode"/>.
/// </summary>
/// <remarks>
/// Only the topics that <see cref="ResearchQueryGate"/> lets go reach the search, and the passage picker sees nothing but these topics and
/// the pages. Searches run in parallel, each once. Every research that ran is stored, a failed one included, so that no later run of the
/// order searches again; the write is conditional, and whoever loses reads the stored research. One log line per research names counts,
/// duration and model only, never topics, passages or addresses.
/// </remarks>
internal sealed class OrderResearchService(
    IOrderRepository orders,
    ITavilySearchClient search,
    IDossierCondenser condenser,
    IOptions<ResearchOptions> options,
    ILogger<OrderResearchService> logger,
    TimeProvider time) : IOrderResearchService
{
    public const string NoKeyCode = "no_key";
    public const string SearchFailedCode = "search_failed";
    public const string CondenseFailedCode = "condense_failed";
    public const string TimeoutCode = "timeout";

    /// <summary>Error code of an unexpected failure of the research itself (for example the daily count could not be read).</summary>
    public const string ErrorCode = "error";

    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<OrderResearch?> EnsureAsync(OrderEntity order, int? orderVersion, bool revises, IReadOnlyList<string> fileTexts, string? profileText, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        var settings = options.Value;
        if (ModeFor(settings, order.CreatedByUser) == ResearchMode.Off)
            return null;

        if (order.ResearchJson is not null)
            return Stored(order);

        if (orderVersion != 1 || revises)
            return null;

        var record = IntakeRecordJson.TryParse(order.ClarifyJson);
        if (record is not { Status: IntakeRecordStatus.Ok } || record.ResearchTopics.Count == 0)
            return null;

        var run = await ResearchAsync(order, record.ResearchTopics, fileTexts ?? [], profileText, settings, ct);
        if (run is null)
            return null;

        var (research, pages, skipped) = run.Value;
        var stored = await StoreAsync(order.Id, research, ct);
        Log(order.Id, research, skipped, stored);
        if (stored == false)
            return await ReadStoredAsync(order.Id, ct);

        if (HasAudit(settings, order.CreatedByUser))
            WriteAudit(settings.AuditDirectory!, order.Id, research, pages);
        return research;
    }

    private async Task<(OrderResearch Research, IReadOnlyList<ResearchPage> Pages, int Skipped)?> ResearchAsync(
        OrderEntity order, IReadOnlyList<string> topics, IReadOnlyList<string> fileTexts, string? profileText, ResearchOptions settings, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var selection = ResearchQueryGate.Select(topics, order.RequestText, fileTexts, profileText, Math.Max(0, settings.MaxSearches));
        var state = new State(selection);
        try
        {
            if (selection.Sent.Count == 0)
                return selection.Withheld > 0 ? (state.End(ResearchStatus.AllWithheld, null, time, started), [], 0) : null;

            var now = time.GetUtcNow();
            var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            if (await orders.CountResearchSinceAsync(dayStart, ct) >= settings.DailyOrderCap)
                return (state.End(ResearchStatus.DailyLimit, null, time, started), [], 0);

            var outcomes = await Task.WhenAll(selection.Sent.Select(topic => SearchOnceAsync(topic, settings.ResultsPerSearch, ct)));
            var attempted = selection.Sent.Where((_, i) => outcomes[i].ErrorCode != NoKeyCode).ToList();
            state.TopicsSent = attempted;
            state.Searches = attempted.Count;
            state.SearchesFailed = outcomes.Count(o => o.ErrorCode is not null and not NoKeyCode);
            var skipped = outcomes.Sum(o => o.Skipped);
            if (outcomes.All(o => o.ErrorCode is not null))
                return (state.End(ResearchStatus.Failed, SearchCode(outcomes), time, started), [], skipped);

            var retrievedAt = time.GetUtcNow();
            var pages = Pages(outcomes);
            state.Pages = pages.Count;
            if (pages.Count == 0)
                return (state.End(ResearchStatus.NothingFound, null, time, started), pages, skipped);

            var condensed = await CondenseOnceAsync(selection.Sent, pages, ct);
            state.Model = condensed.Model;
            if (condensed.ErrorCode is { } code)
                return (state.End(ResearchStatus.Failed, code == TimeoutCode ? TimeoutCode : CondenseFailedCode, time, started), pages, skipped);

            state.Proposed = condensed.Passages.Count + condensed.ParserDropped.Values.Sum();
            var (entries, dropped) = DossierBuilder.Build(condensed.Passages, pages, order.RequestText, settings.MaxEntries, retrievedAt);
            state.Add(condensed.ParserDropped);
            state.Add(dropped);
            state.Entries = entries;
            return (state.End(entries.Count > 0 ? ResearchStatus.Found : ResearchStatus.NothingFound, null, time, started), pages, skipped);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Research failed unexpectedly: {Error}", ex.GetType().Name);
            return (state.End(ResearchStatus.Failed, ErrorCode, time, started), [], 0);
        }
    }

    private async Task<TavilySearchOutcome> SearchOnceAsync(string topic, int maxResults, CancellationToken ct)
    {
        try
        {
            return await search.SearchAsync(topic, maxResults, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new TavilySearchOutcome([], SearchFailedCode, 0);
        }
    }

    private async Task<CondenseOutcome> CondenseOnceAsync(IReadOnlyList<string> topics, IReadOnlyList<ResearchPage> pages, CancellationToken ct)
    {
        try
        {
            return await condenser.CondenseAsync(topics, pages, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new CondenseOutcome([], new Dictionary<ResearchDropReason, int>(), null, CondenseFailedCode, 0);
        }
    }

    private static string SearchCode(IReadOnlyList<TavilySearchOutcome> outcomes) =>
        outcomes.All(o => o.ErrorCode == NoKeyCode) ? NoKeyCode
        : outcomes.All(o => o.ErrorCode == TimeoutCode) ? TimeoutCode
        : SearchFailedCode;

    private static List<ResearchPage> Pages(IReadOnlyList<TavilySearchOutcome> outcomes)
    {
        var pages = new List<ResearchPage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var topic = 0; topic < outcomes.Count; topic++)
        {
            foreach (var hit in outcomes[topic].Hits)
            {
                if (string.IsNullOrWhiteSpace(hit.RawContent) || !seen.Add(hit.Url))
                    continue;
                pages.Add(new ResearchPage($"P{pages.Count + 1}", topic + 1, hit.Url, hit.Title, hit.RawContent));
            }
        }
        return pages;
    }

    private async Task<bool?> StoreAsync(Guid orderId, OrderResearch research, CancellationToken ct)
    {
        try
        {
            return await orders.TrySetResearchAsync(orderId, ResearchJson.Serialize(research), research.At, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Research of order {OrderId} could not be stored ({Error}); this run uses it unstored.", orderId, ex.GetType().Name);
            return null;
        }
    }

    private async Task<OrderResearch?> ReadStoredAsync(Guid orderId, CancellationToken ct)
    {
        try
        {
            var current = await orders.GetAsync(orderId, ct);
            return current is null ? null : Stored(current);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Stored research of order {OrderId} could not be read ({Error}).", orderId, ex.GetType().Name);
            return null;
        }
    }

    private OrderResearch? Stored(OrderEntity order)
    {
        var research = ResearchJson.TryParse(order.ResearchJson);
        if (research is null && order.ResearchJson is not null)
            logger.LogWarning("Stored research of order {OrderId} is unreadable; the run goes on without it.", order.Id);
        return research;
    }

    private void Log(Guid orderId, OrderResearch research, int skipped, bool? stored)
    {
        var dropped = string.Join(',', research.Dropped.OrderBy(d => d.Key).Select(d => $"{d.Key}:{d.Value}"));
        var level = research.Status == ResearchStatus.Failed ? LogLevel.Warning : LogLevel.Information;
        logger.Log(level,
            "Research of order {OrderId}: {Status} (code {Code}), searches {Searches} failed {SearchesFailed}, withheld {Withheld} over limit {OverLimit}, " +
            "pages {Pages} without raw text {Skipped}, proposed {Proposed}, entries {Entries}, dropped [{Dropped}], {DurationMs} ms, model {Model}, stored {Stored}.",
            orderId, research.Status, research.ErrorCode ?? "-", research.Searches, research.SearchesFailed, research.Withheld, research.OverLimit,
            research.Pages, skipped, research.Proposed, research.Entries.Count, dropped, research.DurationMs, research.Model ?? "-",
            stored switch { true => "yes", false => "no, read the stored one", null => "failed" });
    }

    private static ResearchMode ModeFor(ResearchOptions settings, string? user) =>
        user is not null && settings.ModeOverrides.TryGetValue(user, out var mode) ? mode : settings.Mode;

    private static bool HasAudit(ResearchOptions settings, string? user) =>
        !string.IsNullOrWhiteSpace(settings.AuditDirectory) && user is not null && settings.ModeOverrides.ContainsKey(user);

    private void WriteAudit(string directory, Guid orderId, OrderResearch research, IReadOnlyList<ResearchPage> pages)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(directory);
            else
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var record = new AuditRecord(orderId, research.At, research.Status, research.TopicsSent,
                pages.Select(p => new AuditPage(p.Id, p.Topic, p.Url, p.Title, p.RawText, Sha256(p.RawText))).ToList(),
                research.Entries, research.Dropped);
            var path = Path.Combine(directory, $"{orderId:N}-{research.At.UtcDateTime:yyyyMMddHHmmssfff}.json");
            var stream = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                stream.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var file = new FileStream(path, stream);
            JsonSerializer.Serialize(file, record, AuditJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Research audit of order {OrderId} could not be written ({Error}).", orderId, ex.GetType().Name);
        }
    }

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed record AuditPage(string Id, int Topic, string Url, string Title, string RawText, string RawSha256);

    private sealed record AuditRecord(Guid OrderId, DateTimeOffset At, ResearchStatus Status, IReadOnlyList<string> TopicsSent,
        IReadOnlyList<AuditPage> Pages, IReadOnlyList<ResearchEntry> Entries, IReadOnlyDictionary<ResearchDropReason, int> Dropped);

    private sealed class State(QuerySelection selection)
    {
        private readonly SortedDictionary<ResearchDropReason, int> _dropped = new();

        public IReadOnlyList<string> TopicsSent { get; set; } = [];
        public int Searches { get; set; }
        public int SearchesFailed { get; set; }
        public int Pages { get; set; }
        public int Proposed { get; set; }
        public string? Model { get; set; }
        public IReadOnlyList<ResearchEntry> Entries { get; set; } = [];

        public void Add(IReadOnlyDictionary<ResearchDropReason, int> dropped)
        {
            foreach (var (reason, count) in dropped)
                _dropped[reason] = _dropped.GetValueOrDefault(reason) + count;
        }

        public OrderResearch End(ResearchStatus status, string? errorCode, TimeProvider time, long started) =>
            new(OrderResearch.CurrentSchemaVersion, status, TopicsSent, selection.Withheld, selection.OverLimit, Searches, SearchesFailed, Pages,
                Proposed, new Dictionary<ResearchDropReason, int>(_dropped), Entries, Model,
                (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds), errorCode, time.GetUtcNow());
    }
}
