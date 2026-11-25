using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tools.FactCheckEval;
using Geef.Atelier.Tools.IntakeEval;
using Geef.Atelier.Tools.ResearchEval;

namespace Geef.Atelier.Tests.ResearchEvalTool;

/// <summary>Synthetic orders, a simple kit and fake search and picker for the tests of <c>tools/ResearchEval</c>.</summary>
internal static class ResearchEvalTestData
{
    public const string Secret = "tvly-secret-key-0123456789";

    public static ResearchKit Kit { get; } = new(
        "system prompt",
        "pick_passages",
        "{\"type\":\"object\"}",
        (topics, pages) => $"{string.Join('|', topics)}#{pages.Count}",
        Select,
        Build);

    public static RunSettingsRecord Settings { get; } = ResearchKit.Settings(new ResearchOptions());

    public static IReadOnlyList<ResearchOrder> SyntheticOrders(Func<string, IReadOnlyList<string>>? topics = null)
    {
        var gate = new List<StoredGateLine>();
        var corpus = new List<CorpusEntry>();
        foreach (var (prefix, split, count) in new[] { ("d", EvalBasics.Dev, 27), ("h", EvalBasics.Holdout, 9), ("n", EvalBasics.Dev, 4) })
        {
            for (var i = 1; i <= count; i++)
            {
                var id = $"{prefix}-{i:00}";
                gate.Add(new StoredGateLine(id, split, "Ok", prefix == "n" ? [] : topics?.Invoke(id) ?? [$"first topic {id}", $"second topic {id}"]));
                corpus.Add(new CorpusEntry(id, "main", "sparse", "de", $"Request of {id}", [], null, null));
            }
        }
        var (orders, problems) = ResearchCorpus.Load(gate, corpus);
        Assert.Empty(problems);
        return orders;
    }

    public static string Raw(string query) => $"Page about {query}.\nThe answer to {query} is forty two euros per hour. Nothing else.";

    public static string Quote(string query) => $"The answer to {query} is forty two euros per hour.";

    public static Func<string, int, CancellationToken, Task<(IReadOnlyList<(string Url, string Title, string? RawText)> Hits, string? ErrorCode, int DurationMs)>> Search(
        ResearchTransport transport, Func<string, string?>? error = null) =>
        (query, _, _) =>
        {
            if (!transport.TakeSearch(TavilyProbe.Body(query, null)))
                return Task.FromResult<(IReadOnlyList<(string, string, string?)>, string?, int)>(([], "refused", 1));
            if (error?.Invoke(query) is { } code)
                return Task.FromResult<(IReadOnlyList<(string, string, string?)>, string?, int)>(([], code, 20_000));
            IReadOnlyList<(string, string, string?)> hits = [($"https://pages.example/{query.Replace(' ', '-')}", $"Title {query}", Raw(query)), ("https://empty.example/", "No raw text", null)];
            return Task.FromResult<(IReadOnlyList<(string, string, string?)>, string?, int)>((hits, null, 800));
        };

    public static Func<IReadOnlyList<string>, IReadOnlyList<ResearchPage>, CancellationToken, Task<(IReadOnlyList<ProposedPassage> Passages, IReadOnlyDictionary<ResearchDropReason, int> ParserDropped, string? Model, string? ErrorCode, int DurationMs)>> Condense(
        ResearchTransport transport, Func<IReadOnlyList<string>, string?>? error = null, bool failoverOff = true) =>
        (topics, pages, _) =>
        {
            var request = new LlmRequest { Model = "claude-sonnet-latest", SystemPrompt = "s", UserPrompt = "u", DisableFailover = failoverOff };
            if (!transport.TakeCall(request))
                return Task.FromResult<(IReadOnlyList<ProposedPassage>, IReadOnlyDictionary<ResearchDropReason, int>, string?, string?, int)>(([], new Dictionary<ResearchDropReason, int>(), null, "refused", 1));
            if (error?.Invoke(topics) is { } code)
            {
                transport.Answered(code == "timeout" ? null : "not json");
                return Task.FromResult<(IReadOnlyList<ProposedPassage>, IReadOnlyDictionary<ResearchDropReason, int>, string?, string?, int)>(([], new Dictionary<ResearchDropReason, int>(), "claude-sonnet-5", code, 60_000));
            }
            var passages = pages.Select(p => new ProposedPassage(p.Topic, p.Id, Quote(topics[p.Topic - 1])))
                .Append(new ProposedPassage(1, pages[0].Id, "An invented sentence that is on no page."))
                .ToList();
            transport.Answered("{\"passages\":[]}");
            return Task.FromResult<(IReadOnlyList<ProposedPassage>, IReadOnlyDictionary<ResearchDropReason, int>, string?, string?, int)>(
                (passages, new Dictionary<ResearchDropReason, int> { [ResearchDropReason.UnknownPage] = 1 }, "claude-sonnet-5", null, 9_000));
        };

    public static ResearchTransport Transport(int searches = 84, int calls = 33, int searchesUsed = 0, int callsUsed = 0) =>
        new(new CallBudget(searches, ResearchRunner.SearchCeiling, searchesUsed), new CallBudget(calls, ResearchRunner.ClaudeCeiling, callsUsed));

    public static ResearchRunner Runner(string outPath, string set, IReadOnlyList<ResearchOrder> orders, ResearchTransport transport, ICodexGuard? guard = null,
        Func<string, string?>? searchError = null, Func<IReadOnlyList<string>, string?>? condenseError = null, bool failoverOff = true, ResearchKit? kit = null) =>
        new(new ResearchRunSettings(outPath, set, new ResearchOptions()), orders, Search(transport, searchError), Condense(transport, condenseError, failoverOff),
            transport, guard ?? new OpenGuard(), kit ?? Kit, TimeProvider.System, TextWriter.Null, [Secret]);


    private static QuerySelection Select(IReadOnlyList<string> topics, string request, IReadOnlyList<string> files, string? profile, int max)
    {
        var allowed = topics.Where(t => !t.Contains("withheld", StringComparison.Ordinal)).ToList();
        return new QuerySelection(allowed.Take(max).ToList(), topics.Count - allowed.Count, Math.Max(0, allowed.Count - max));
    }

    private static (IReadOnlyList<ResearchEntry> Entries, IReadOnlyDictionary<ResearchDropReason, int> Dropped) Build(
        IReadOnlyList<ProposedPassage> proposed, IReadOnlyList<ResearchPage> pages, string request, int max, DateTimeOffset at)
    {
        var entries = new List<ResearchEntry>();
        var dropped = new Dictionary<ResearchDropReason, int>();
        foreach (var passage in proposed)
        {
            var page = pages.FirstOrDefault(p => p.Id == passage.PageId);
            var start = page?.RawText.IndexOf(passage.Quote, StringComparison.Ordinal) ?? -1;
            var reason = page is null || start < 0 ? ResearchDropReason.NotInPage : entries.Count >= max ? ResearchDropReason.OverLimit : (ResearchDropReason?)null;
            if (reason is { } r)
            {
                dropped[r] = dropped.GetValueOrDefault(r) + 1;
                continue;
            }
            entries.Add(new ResearchEntry($"W1.{entries.Count + 1}", passage.Topic, passage.Quote, page!.Url, page.Title, at, QuoteMatchKind.Exact,
                JsonLines.Sha256(page.RawText), start, passage.Quote.Length));
        }
        return (entries, dropped);
    }
}

/// <summary>A guard that never blocks.</summary>
internal sealed class OpenGuard : ICodexGuard
{
    public string? Blocker() => null;
}

/// <summary>A guard that always blocks.</summary>
internal sealed class ClosedGuard : ICodexGuard
{
    public string? Blocker() => "a Codex process runs";
}

/// <summary>A temporary folder of one test; every file gets its own subfolder, everything is deleted on dispose.</summary>
internal sealed class TempFolder : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("research-eval-").FullName;

    public string File(string name) => Path.Combine(Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N"))).FullName, name);

    public string Root => _root;

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
