using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Infrastructure.Research;
using Geef.Atelier.Tools.FactCheckEval;

namespace Geef.Atelier.Tools.ResearchEval;

/// <summary>Settings of one <c>run</c>.</summary>
public sealed record ResearchRunSettings(string OutPath, string Set, ResearchOptions Options);

/// <summary>Status names and error codes of a VR line (the names of <see cref="ResearchStatus"/> and the codes of the research service).</summary>
public static class RunStatus
{
    /// <summary>Error code of an order whose searches all failed.</summary>
    public const string SearchFailed = OrderResearchService.SearchFailedCode;

    /// <summary>Error code of a passage picker call without usable answer.</summary>
    public const string CondenseFailed = OrderResearchService.CondenseFailedCode;

    /// <summary>Error code of a passage picker call that ran into its time limit.</summary>
    public const string Timeout = OrderResearchService.TimeoutCode;

    /// <summary>True for a line whose duration is a real duration of search and picking: a result, an answer that was unusable, or a time-out.</summary>
    public static bool HasLatency(ResearchRunLine line) =>
        line.Status is nameof(ResearchStatus.Found) or nameof(ResearchStatus.NothingFound)
        || line.ErrorCode == Timeout
        || (line.ErrorCode == CondenseFailed && line.Condense?.Raw is not null);

    /// <summary>True when the passage picker was called and gave a usable answer.</summary>
    public static bool ValidAnswer(ResearchRunLine line) => line.Condense is { ErrorCode: null };
}

/// <summary>
/// Runs the real research parts for every order of one set (contract stage 4, 3.3), one order after another, and appends one line per
/// order. Before every order the <see cref="ICodexGuard"/> is asked (exit 3 while it blocks; continue later with the same command), and
/// the order starts only when both budgets cover it completely (all its searches and one model call). Each order is measured once per
/// result file: a line for the current input, whatever its status, is never repeated, so repeating a command never improves a figure.
/// Lines for another input make the run refuse (start a new file).
/// </summary>
public sealed class ResearchRunner(
    ResearchRunSettings settings,
    IReadOnlyList<ResearchOrder> orders,
    Func<string, int, CancellationToken, Task<(IReadOnlyList<(string Url, string Title, string? RawText)> Hits, string? ErrorCode, int DurationMs)>> search,
    Func<IReadOnlyList<string>, IReadOnlyList<ResearchPage>, CancellationToken, Task<(IReadOnlyList<ProposedPassage> Passages, IReadOnlyDictionary<ResearchDropReason, int> ParserDropped, string? Model, string? ErrorCode, int DurationMs)>> condense,
    ResearchTransport transport,
    ICodexGuard guard,
    ResearchKit kit,
    TimeProvider time,
    TextWriter log,
    IReadOnlyList<string?> secrets)
{
    /// <summary>Most Tavily requests one result file may hold (contract stage 4, 3.3: 21 orders × 4).</summary>
    public const int SearchCeiling = 84;

    /// <summary>Most passage picker calls one result file may hold (contract stage 4, 3.3: 21 + 12 for one prompt round).</summary>
    public const int ClaudeCeiling = 33;

    /// <summary>Exit code when the guard stopped the run before an order.</summary>
    public const int Blocked = 3;

    /// <summary>Exit code when orders are left because a cap is reached.</summary>
    public const int Incomplete = 4;

    /// <summary>Exit code when the run refuses to start or the transport refused a request.</summary>
    public const int Refused = 2;

    /// <summary>Requests and calls already in a result file.</summary>
    public static (int Searches, int Calls) Used(IReadOnlyList<ResearchRunLine> lines) => (lines.Sum(l => l.TavilyRequests), lines.Sum(l => l.ClaudeCalls));

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var record = ResearchKit.Settings(settings.Options);
        var set = ResearchCorpus.Set(orders, settings.Set);
        var hashes = set.ToDictionary(o => o.Id, o => kit.InputHash(o, record), StringComparer.Ordinal);
        var previous = JsonLines.Load<ResearchRunLine>(settings.OutPath);
        var stale = previous.Where(l => hashes.TryGetValue(l.Id, out var h) && l.InputHash != h).Select(l => l.Id).Distinct(StringComparer.Ordinal).ToList();
        if (stale.Count > 0)
        {
            log.WriteLine($"{settings.OutPath} holds lines for another input ({EvalBasics.Few(stale)}); start a new --out file.");
            return Refused;
        }

        var done = previous.Where(l => hashes.ContainsKey(l.Id)).Select(l => l.Id).ToHashSet(StringComparer.Ordinal);
        var todo = set.Where(o => !done.Contains(o.Id)).ToList();
        log.WriteLine($"{todo.Count} of {set.Count} orders of set {settings.Set} to measure; {transport.Searches.Remaining} searches and {transport.Claude.Remaining} model calls left in this run.");

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var order in todo)
        {
            if (guard.Blocker() is { } blocker)
            {
                log.WriteLine($"Stopped before {order.Id}: {blocker}. Continue later with the same command.");
                return Blocked;
            }

            var line = await MeasureAsync(order, hashes[order.Id], record, ct);
            if (line is null)
            {
                log.WriteLine($"Cap reached before {order.Id}; {todo.Count - todo.IndexOf(order)} orders left (searches left {transport.Searches.Remaining}, model calls left {transport.Claude.Remaining}).");
                return Incomplete;
            }

            JsonLines.Append(settings.OutPath, line, secrets);
            counts[line.Status] = counts.GetValueOrDefault(line.Status) + 1;
            log.WriteLine($"{order.Id}: {line.Status}{(line.ErrorCode is null ? string.Empty : $" ({line.ErrorCode})")}, {line.Sent.Count} sent, {line.Withheld} withheld, {line.Pages.Count} pages, {line.Entries.Count} entries, {line.DurationMs} ms");
            if (transport.Refusal is { } refusal)
            {
                log.WriteLine($"Stopped after {order.Id}: {refusal}. The line is kept and counts; fix the cause before measuring on.");
                return Refused;
            }
        }

        log.WriteLine($"Finished set {settings.Set}: {string.Join(", ", counts.Select(c => $"{c.Value} {c.Key}"))}.");
        return 0;
    }

    private async Task<ResearchRunLine?> MeasureAsync(ResearchOrder order, string inputHash, RunSettingsRecord record, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var options = settings.Options;
        var selection = kit.Select(order.Topics, order.Request, order.FileTexts, order.ProfileText, options.MaxSearches);
        var retrievedAt = time.GetUtcNow();
        if (selection.Sent.Count == 0)
        {
            transport.BeginOrder([]);
            return Line(order, inputHash, record, nameof(ResearchStatus.AllWithheld), null, selection, [], [], null, [], new Dictionary<string, int>(), 0, started, retrievedAt);
        }
        if (transport.Searches.Remaining < selection.Sent.Count || transport.Claude.Remaining < 1)
            return null;

        transport.BeginOrder(selection.Sent);
        var searchStarted = time.GetTimestamp();
        var outcomes = await Task.WhenAll(selection.Sent.Select(topic => search(topic, options.ResultsPerSearch, ct)));
        var searchMs = Elapsed(searchStarted);
        var searches = outcomes.Select((o, i) => new SearchRecord(i + 1, o.ErrorCode, o.DurationMs, o.Hits.Count(h => !string.IsNullOrWhiteSpace(h.RawText)))).ToList();
        var pages = ResearchPages.From(outcomes.Select(o => o.Hits).ToList());
        var pageRecords = pages.Select(p => new PageRecord(p.Id, p.Topic, p.Url, p.Title, p.RawText, JsonLines.Sha256(p.RawText))).ToList();

        if (outcomes.All(o => o.ErrorCode is not null))
            return Line(order, inputHash, record, nameof(ResearchStatus.Failed), RunStatus.SearchFailed, selection, searches, pageRecords, null, [], new Dictionary<string, int>(), searchMs, started, retrievedAt);
        if (pages.Count == 0)
            return Line(order, inputHash, record, nameof(ResearchStatus.NothingFound), null, selection, searches, pageRecords, null, [], new Dictionary<string, int>(), searchMs, started, retrievedAt);

        var picked = await condense(selection.Sent, pages, ct);
        var condenseRecord = new CondenseRecord(picked.Model, picked.ErrorCode, picked.DurationMs, transport.LastRaw,
            picked.Passages.Select(p => new PassageRecord(p.Topic, p.PageId, p.Quote)).ToList(), Names(picked.ParserDropped));
        if (picked.ErrorCode is not null)
        {
            var code = picked.ErrorCode == RunStatus.Timeout ? RunStatus.Timeout : RunStatus.CondenseFailed;
            return Line(order, inputHash, record, nameof(ResearchStatus.Failed), code, selection, searches, pageRecords, condenseRecord, [], Names(picked.ParserDropped), searchMs, started, retrievedAt);
        }

        var (entries, dropped) = kit.Build(picked.Passages, pages, order.Request, options.MaxEntries, retrievedAt);
        var allDropped = Names(picked.ParserDropped);
        foreach (var (reason, count) in Names(dropped))
            allDropped[reason] = allDropped.GetValueOrDefault(reason) + count;
        var entryRecords = entries.Select(e => new EntryRecord(e.Id, e.Topic, e.Quote, e.Url, e.Title, e.RetrievedAt, e.Match.ToString(), e.RawSha256, e.Start, e.Length)).ToList();
        var status = entries.Count > 0 ? nameof(ResearchStatus.Found) : nameof(ResearchStatus.NothingFound);
        return Line(order, inputHash, record, status, null, selection, searches, pageRecords, condenseRecord, entryRecords, allDropped, searchMs, started, retrievedAt);
    }

    private ResearchRunLine Line(ResearchOrder order, string inputHash, RunSettingsRecord record, string status, string? errorCode, QuerySelection selection,
        IReadOnlyList<SearchRecord> searches, IReadOnlyList<PageRecord> pages, CondenseRecord? picked, IReadOnlyList<EntryRecord> entries,
        Dictionary<string, int> dropped, int searchMs, long started, DateTimeOffset retrievedAt) =>
        new(order.Id, settings.Set, status, errorCode, order.Topics, selection.Sent, selection.Withheld, selection.OverLimit,
            transport.TavilyRequests, transport.ClaudeCalls, searches, pages, picked, entries, dropped, searchMs, Elapsed(started), retrievedAt,
            record, inputHash, time.GetUtcNow());

    private int Elapsed(long started) => (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds);

    private static Dictionary<string, int> Names(IReadOnlyDictionary<ResearchDropReason, int> dropped) =>
        dropped.Where(d => d.Value > 0).ToDictionary(d => d.Key.ToString(), d => d.Value, StringComparer.Ordinal);
}

/// <summary>Adapters from the real search client and passage picker to the shapes the runner takes.</summary>
public static class RealResearchParts
{
    internal static Func<string, int, CancellationToken, Task<(IReadOnlyList<(string Url, string Title, string? RawText)> Hits, string? ErrorCode, int DurationMs)>> Search(ITavilySearchClient client) =>
        async (query, max, ct) =>
        {
            var outcome = await client.SearchAsync(query, max, ct);
            return (outcome.Hits.Select(h => (h.Url, h.Title, h.RawContent)).ToList(), outcome.ErrorCode, outcome.DurationMs);
        };

    internal static Func<IReadOnlyList<string>, IReadOnlyList<ResearchPage>, CancellationToken, Task<(IReadOnlyList<ProposedPassage> Passages, IReadOnlyDictionary<ResearchDropReason, int> ParserDropped, string? Model, string? ErrorCode, int DurationMs)>> Condense(IDossierCondenser condenser) =>
        async (topics, pages, ct) =>
        {
            var outcome = await condenser.CondenseAsync(topics, pages, ct);
            return (outcome.Passages, outcome.ParserDropped, outcome.Model, outcome.ErrorCode, outcome.DurationMs);
        };
}
