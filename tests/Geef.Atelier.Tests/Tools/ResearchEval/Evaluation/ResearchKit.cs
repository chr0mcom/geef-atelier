using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Infrastructure.Research;
using Geef.Atelier.Tools.FactCheckEval;

namespace Geef.Atelier.Tools.ResearchEval;

/// <summary>
/// The parts of the real research the measurement uses: prompt and schema of the passage picker (for the input fingerprint), the query
/// gate, the page numbering and the dossier builder. <see cref="Real"/> is the production code; tests swap single parts.
/// </summary>
public sealed record ResearchKit(
    string SystemPrompt,
    string SchemaName,
    string SchemaJson,
    Func<IReadOnlyList<string>, IReadOnlyList<ResearchPage>, string> UserPrompt,
    Func<IReadOnlyList<string>, string, IReadOnlyList<string>, string?, int, QuerySelection> Select,
    Func<IReadOnlyList<ProposedPassage>, IReadOnlyList<ResearchPage>, string, int, DateTimeOffset, (IReadOnlyList<ResearchEntry> Entries, IReadOnlyDictionary<ResearchDropReason, int> Dropped)> Build)
{
    /// <summary>The production code: <c>DossierPrompts</c>, <c>DossierSchema</c>, <c>ResearchQueryGate</c>, <c>DossierBuilder</c>.</summary>
    public static ResearchKit Real { get; } = new(
        DossierPrompts.System,
        DossierSchema.Name,
        DossierSchema.Json,
        DossierPrompts.User,
        ResearchQueryGate.Select,
        DossierBuilder.Build);

    private static readonly IReadOnlyList<string> ProbeTopics = ["probe topic"];

    private static readonly IReadOnlyList<ResearchPage> ProbePages =
        [new("P1", 1, "https://probe.example/page", "Probe <title>", "Probe text with a <tag> and some words.\nSecond line.")];

    /// <summary>
    /// Fingerprint of everything that decides what an order sends and gets: system prompt, schema name, schema, the user prompt
    /// for a fixed probe input (so a change of its template shows), the stored topics of the order and the research settings. A
    /// change of any of them makes older lines not match.
    /// </summary>
    public string InputHash(ResearchOrder order, RunSettingsRecord settings) => EvalBasics.Fingerprint(
        SystemPrompt, SchemaName, SchemaJson, UserPrompt(ProbeTopics, ProbePages), string.Join('\n', order.Topics),
        $"{settings.MaxSearches}/{settings.ResultsPerSearch}/{settings.RawCharsPerPage}/{settings.MaxEntries}/{settings.SearchTimeoutMs}/{settings.CondenseTimeoutMs}");

    /// <summary>The settings record of <paramref name="options"/>.</summary>
    public static RunSettingsRecord Settings(ResearchOptions options) => new(
        options.MaxSearches, options.ResultsPerSearch, options.RawCharsPerPage, options.MaxEntries,
        (int)options.SearchTimeout.TotalMilliseconds, (int)options.CondenseTimeout.TotalMilliseconds);
}

/// <summary>Turns search results into the numbered pages the passage picker sees.</summary>
public static class ResearchPages
{
    /// <summary>
    /// Pages <c>P1…</c> in the order of the topics and, per topic, of the results; results without raw text are left out. The
    /// numbering follows contract stage 4, 4.4; when the research service of F2 numbers differently, this is the place to align.
    /// </summary>
    public static IReadOnlyList<ResearchPage> From(IReadOnlyList<IReadOnlyList<(string Url, string Title, string? RawText)>> resultsPerTopic)
    {
        var pages = new List<ResearchPage>();
        for (var topic = 0; topic < resultsPerTopic.Count; topic++)
        {
            foreach (var (url, title, raw) in resultsPerTopic[topic])
            {
                if (!string.IsNullOrWhiteSpace(raw))
                    pages.Add(new ResearchPage($"P{pages.Count + 1}", topic + 1, url, title, raw));
            }
        }
        return pages;
    }
}
