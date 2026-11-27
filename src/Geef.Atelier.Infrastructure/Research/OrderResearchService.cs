using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Research;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
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
/// Only orders of the standard way search (<see cref="OrderMode.Direct"/>); "Extra careful" never does. An order with attached files
/// never searches (<see cref="ResearchStatus.FilesAttached"/>). Topics the intake already withheld against the About me details the
/// clarifier read (<see cref="IntakeRecord.ResearchWithheld"/>) count as withheld. Otherwise only the topics that
/// <see cref="ResearchQueryGate"/> lets go reach the search: it gets the values of the About me details without their field names
/// (<see cref="ProfilePrompt.Values"/>) and, when those hold free text, the language of the order. The passage picker sees nothing but
/// these topics and the pages. A search is reserved before it starts (<see cref="IOrderRepository.TryReserveResearchAsync"/>, daily
/// limit in total and per account). Searches run in parallel, each once. Every research that ran is stored, a failed one included, so
/// that no later run of the order searches again; the write is conditional, and whoever loses reads the stored research. Research that
/// cannot be stored is not used: the run goes on as failed, without passages. One log line per research names counts, duration and
/// model only, never topics, passages or addresses.
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

    /// <summary>Error code of research that ran but could not be stored; the run goes on without it.</summary>
    public const string NotStoredCode = "not_stored";

    public async Task<OrderResearch?> EnsureAsync(OrderEntity order, int? orderVersion, bool revises, bool hasFiles, string? profileText, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        var settings = options.Value;
        if (order.Mode != OrderMode.Direct || ModeFor(settings, order.CreatedByUser) == ResearchMode.Off)
            return null;

        if (hasFiles)
            return await FilesAttachedAsync(order, ct);

        if (order.ResearchJson is not null)
            return Stored(order);

        if (orderVersion != 1 || revises)
            return null;

        var record = IntakeRecordJson.TryParse(order.ClarifyJson);
        var topics = Topics(record);
        var withheldAtIntake = WithheldAtIntake(record);
        if (topics.Count == 0 && withheldAtIntake == 0)
            return null;

        var run = await ResearchAsync(order, topics, withheldAtIntake, profileText, record!.Output?.Language, settings, ct);
        if (run is null)
            return null;

        var (research, pages, skipped) = run.Value;
        var stored = await StoreAsync(order.Id, research, ct);
        Log(order.Id, research, skipped, stored);
        if (stored == false)
            return await ReadStoredAsync(order.Id, ct);
        if (stored is null)
            return Unstored(research);

        if (HasAudit(settings, order.CreatedByUser))
            WriteAudit(settings.AuditDirectory!, order.Id, research, pages);
        return research;
    }

    /// <summary>
    /// The research topics of a clarified order that may be looked up: those of the record, without a topic that is nothing but the
    /// name of a kind of gap (such a topic says nothing to search for).
    /// </summary>
    internal static IReadOnlyList<string> Topics(IntakeRecord? record) =>
        record is { Status: IntakeRecordStatus.Ok }
            ? record.ResearchTopics
                .Where(t => !string.IsNullOrWhiteSpace(t) && !Enum.GetValues<GapTopic>().Any(g => string.Equals(g.Label(), t.Trim(), StringComparison.OrdinalIgnoreCase)))
                .ToList()
            : [];

    /// <summary>The research topics the intake withheld against the About me details the clarifier read (<see cref="IntakeRecord.ResearchWithheld"/>).</summary>
    internal static int WithheldAtIntake(IntakeRecord? record) =>
        record is { Status: IntakeRecordStatus.Ok, ResearchWithheld: > 0 and var withheld } ? withheld : 0;

    /// <summary>
    /// An order with attached files never searches (D-074): nothing of a file can reach the search or the passage picker. When the order
    /// has research topics, the status <see cref="ResearchStatus.FilesAttached"/> is stored once, so that every run of the order shows
    /// it; stored research of another status is not used for such an order.
    /// </summary>
    private async Task<OrderResearch?> FilesAttachedAsync(OrderEntity order, CancellationToken ct)
    {
        var stored = order.ResearchJson is null ? null : Stored(order);
        if (stored is { Status: ResearchStatus.FilesAttached })
            return stored;
        var record = IntakeRecordJson.TryParse(order.ClarifyJson);
        if (Topics(record).Count == 0 && WithheldAtIntake(record) == 0)
            return null;

        var research = new OrderResearch(OrderResearch.CurrentSchemaVersion, ResearchStatus.FilesAttached, [], 0, 0, 0, 0, 0, 0,
            new Dictionary<ResearchDropReason, int>(), [], null, 0, null, time.GetUtcNow());
        if (order.ResearchJson is null && await StoreAsync(order.Id, research, ct) == false
            && await ReadStoredAsync(order.Id, ct) is { Status: ResearchStatus.FilesAttached } other)
            return other;
        return research;
    }

    /// <summary>Research that could not be stored: the run goes on without its passages, as failed, so a warning says so.</summary>
    private static OrderResearch Unstored(OrderResearch research) =>
        research with { Status = ResearchStatus.Failed, Entries = [], ErrorCode = NotStoredCode };

    private async Task<(OrderResearch Research, IReadOnlyList<ResearchPage> Pages, int Skipped)?> ResearchAsync(
        OrderEntity order, IReadOnlyList<string> topics, int withheldAtIntake, string? profileText, string? orderLanguage, ResearchOptions settings, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var profile = ProfilePrompt.Values(profileText);
        var gate = ResearchQueryGate.Select(topics, order.RequestText, [], profile.Text, Math.Max(0, settings.MaxSearches),
            profile.HasFreeText ? orderLanguage : null);
        var selection = gate with { Withheld = gate.Withheld + withheldAtIntake };
        var state = new State(selection);
        try
        {
            if (selection.Sent.Count == 0)
                return selection.Withheld > 0 ? (state.End(ResearchStatus.AllWithheld, null, time, started), [], 0) : null;

            var now = time.GetUtcNow();
            var dayStart = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            if (!await orders.TryReserveResearchAsync(order.Id, order.CreatedByUser, now, dayStart, settings.DailyOrderCap, settings.DailyOrderCapPerUser, ct))
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
            var pages = outcomes.Select(o => o.Hits).ToList().ToResearchPages();
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

    private async Task<bool?> StoreAsync(Guid orderId, OrderResearch research, CancellationToken ct)
    {
        try
        {
            return await orders.TrySetResearchAsync(orderId, ResearchJson.Serialize(research), null, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Research of order {OrderId} could not be stored ({Error}); the run goes on without it.", orderId, ex.GetType().Name);
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

    private static ResearchMode ModeFor(ResearchOptions settings, string? user) => settings.ModeFor(user);

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
