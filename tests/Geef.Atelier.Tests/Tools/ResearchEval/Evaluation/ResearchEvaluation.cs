using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Tools.FactCheckEval;
using static System.FormattableString;

namespace Geef.Atelier.Tools.ResearchEval;

/// <summary>A result file of VR with the SHA-256 of its bytes (the independent check names the files it read by it).</summary>
public sealed record RunFile(string Path, string Sha256, IReadOnlyList<ResearchRunLine> Lines);

/// <summary>A file the independent check read.</summary>
public sealed record VerifiedFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("lines")] int Lines,
    [property: JsonPropertyName("entries")] int Entries);

/// <summary>An entry the independent check did not find word for word in its raw text.</summary>
public sealed record Violation(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("order")] string Order,
    [property: JsonPropertyName("entry")] string Entry,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>Output of <c>tools/Eval/geef_eval/research_verify.py</c>.</summary>
public sealed record VerificationReport(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("files")] IReadOnlyList<VerifiedFile> Files,
    [property: JsonPropertyName("entries")] int Entries,
    [property: JsonPropertyName("violations")] IReadOnlyList<Violation> Violations);

/// <summary>
/// Gate VR (contract stage 4, 3.3 and 8) over stored results, without any call. The gate never passes without data: it is not
/// evaluable when a held-out order has no line or more than one for the current input, a line lacks the input fingerprint or has
/// another one, fewer than 7 of 9 held-out orders got a valid answer of the passage picker, raw texts are missing, the independent
/// check is missing or read other files, the query gate or the dossier builder now decide differently than in the run, or a held-out
/// entry has no judgement. K1 is hard: one entry not found word for word (by the independent check or the own recheck) fails the
/// gate, whatever else is missing.
/// </summary>
public static partial class ResearchEvaluation
{
    /// <summary>Name of the gate.</summary>
    public const string Gate = "VR";

    /// <summary>Held-out orders that need a valid answer of the passage picker.</summary>
    public const int MinValidOrders = 7;

    /// <summary>Lowest share of valid passage picker answers.</summary>
    public const double MinValidity = 0.95;

    /// <summary>Lowest share of held-out orders with at least one entry.</summary>
    public const double MinYield = 0.70;

    /// <summary>Lowest share of held-out entries that help with their topic.</summary>
    public const double MinRelevance = 0.80;

    /// <summary>Highest median duration of search and picking per order.</summary>
    public const int MaxMedianMs = 60_000;

    /// <summary>Name of the independent check in its report.</summary>
    public const string VerifierName = "research_verify";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    /// <summary>White space collapsed to single spaces and trimmed; case is kept.</summary>
    public static string Collapse(string text) => Space().Replace(text, " ").Trim();

    /// <summary>
    /// The own recheck of every entry against the stored raw text of its page: page found by its SHA-256 with the same address, the
    /// stored range inside the raw text and, white space collapsed, equal to the quote. Lines with entries but no stored page at all
    /// are reported as missing raw texts, not as violations.
    /// </summary>
    public static (IReadOnlyList<string> Violations, IReadOnlyList<string> MissingRaw, int Entries) Recheck(IReadOnlyList<RunFile> files)
    {
        var violations = new List<string>();
        var missing = new List<string>();
        var count = 0;
        foreach (var file in files)
        {
            foreach (var line in file.Lines)
            {
                count += line.Entries.Count;
                if (line.Entries.Count > 0 && line.Pages.Count == 0)
                {
                    missing.Add($"{line.Id} in {file.Path}");
                    continue;
                }
                foreach (var entry in line.Entries)
                {
                    if (Check(line, entry) is { } reason)
                        violations.Add($"{line.Id}/{entry.Id} in {file.Path}: {reason}");
                }
            }
        }
        return (violations, missing, count);
    }

    private static string? Check(ResearchRunLine line, EntryRecord entry)
    {
        var quote = Collapse(entry.Quote);
        if (quote.Length == 0)
            return "empty quote";
        var pages = line.Pages.Where(p => string.Equals(JsonLines.Sha256(p.RawText), entry.RawSha256, StringComparison.OrdinalIgnoreCase)).ToList();
        if (pages.Count == 0)
            return "no stored page has the raw-text hash of the entry";
        if (!pages.Any(p => p.Url == entry.Url))
            return "the page with this raw text has another address";
        var raw = pages[0].RawText;
        if (entry.Start < 0 || entry.Length <= 0 || entry.Start + entry.Length > raw.Length)
            return "the stored range lies outside the raw text";
        if (Collapse(raw.Substring(entry.Start, entry.Length)) != quote)
            return "the stored range does not hold the quote";
        if (CutsWordOrNumber(raw, entry.Start) || CutsWordOrNumber(raw, entry.Start + entry.Length))
            return "the stored range starts or ends inside a word or a number";
        return Collapse(raw).Contains(quote, StringComparison.Ordinal) ? null : "the quote is not in the raw text";
    }

    /// <summary>
    /// True when the boundary before <paramref name="index"/> of <paramref name="raw"/> lies inside a word (letters, digits, soft
    /// hyphen, combining marks on both sides) or inside a number written with a dot or comma between digits.
    /// </summary>
    internal static bool CutsWordOrNumber(string raw, int index)
    {
        static bool Part(char c) => char.IsLetterOrDigit(c) || c == '\u00AD'
            || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark or System.Globalization.UnicodeCategory.EnclosingMark;
        static bool Mark(char c) => c is '.' or ',';
        if (index <= 0 || index >= raw.Length)
            return false;
        return (Part(raw[index - 1]) && Part(raw[index]))
            || (index >= 2 && Mark(raw[index - 1]) && char.IsDigit(raw[index - 2]) && char.IsDigit(raw[index]))
            || (index + 1 < raw.Length && Mark(raw[index]) && char.IsDigit(raw[index - 1]) && char.IsDigit(raw[index + 1]));
    }

    /// <summary>
    /// Every part of gate VR on the held-out set and the overall verdict (last), plus the figures that are reported but not gated
    /// and the development figures (never a verdict).
    /// </summary>
    public static (IReadOnlyList<GateResult> Verdicts, IReadOnlyList<string> Figures) Evaluate(
        IReadOnlyList<ResearchOrder> orders, RunFile holdout, IReadOnlyList<RunFile> also, IReadOnlyList<JudgeLine> judgments,
        VerificationReport? verification, ResearchKit kit, RunSettingsRecord settings)
    {
        var problems = new List<string>();
        var figures = new List<string>();
        var holdoutOrders = ResearchCorpus.Set(orders, ResearchCorpus.HoldoutSet);
        var byId = orders.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var hashes = orders.ToDictionary(o => o.Id, o => kit.InputHash(o, settings), StringComparer.Ordinal);
        var allFiles = new[] { holdout }.Concat(also).ToList();

        var lines = new List<ResearchRunLine>();
        foreach (var order in holdoutOrders)
        {
            var mine = allFiles.SelectMany(f => f.Lines).Where(l => l.Id == order.Id).ToList();
            var current = mine.Where(l => l.InputHash == hashes[order.Id]).ToList();
            if (mine.Any(l => l.InputHash is null))
                problems.Add($"{order.Id} has a line without input fingerprint");
            if (mine.Count > current.Count)
                problems.Add($"{order.Id} has lines for another input");
            if (current.Count == 0)
                problems.Add($"{order.Id} has no line for the current input");
            else if (current.Count > 1)
                problems.Add($"{order.Id} was measured {current.Count} times (the held-out set is measured once; evaluate the new file alone)");
            else if (!holdout.Lines.Contains(current[0]))
                problems.Add($"the line of {order.Id} is not in the held-out file {holdout.Path}");
            else
                lines.Add(current[0]);
        }
        var foreign = allFiles.SelectMany(f => f.Lines).Where(l => ResearchCorpus.SetOf(orders, l.Id) is null).Select(l => l.Id).Distinct(StringComparer.Ordinal).ToList();
        if (foreign.Count > 0)
            problems.Add($"lines for orders outside VR (reserved for R3): {EvalBasics.Few(foreign)}");

        foreach (var line in lines)
        {
            var order = byId[line.Id];
            var selection = kit.Select(order.Topics, order.Request, order.FileTexts, order.ProfileText, settings.MaxSearches);
            if (!selection.Sent.SequenceEqual(line.Sent, StringComparer.Ordinal) || selection.Withheld != line.Withheld || selection.OverLimit != line.OverLimit)
                problems.Add($"{line.Id}: the query gate now sends other topics than in the run");
            if (RunStatus.ValidAnswer(line) && !Rebuilds(line, order, kit, settings))
                problems.Add($"{line.Id}: the dossier builder now builds other entries from the stored proposals than in the run");
        }

        var valid = lines.Count(RunStatus.ValidAnswer);
        if (valid < MinValidOrders)
            problems.Add($"only {valid} of {holdoutOrders.Count} held-out orders have a valid answer of the passage picker (at least {MinValidOrders} needed)");

        var (ownViolations, missingRaw, entryCount) = Recheck(allFiles);
        if (missingRaw.Count > 0)
            problems.Add($"raw texts missing for entries of {EvalBasics.Few(missingRaw)}");
        var verificationProblem = VerificationProblem(verification, allFiles, entryCount);

        var k1 = K1(ownViolations, verification, verificationProblem, missingRaw, entryCount);
        var parts = new List<GateResult> { k1 };
        if (problems.Count > 0)
        {
            var reason = string.Join("; ", problems);
            parts.Add(GateResult.NotEvaluable(Gate, "validity", reason));
            parts.Add(GateResult.NotEvaluable(Gate, "yield", reason));
            parts.Add(GateResult.NotEvaluable(Gate, "relevance", reason));
            parts.Add(GateResult.NotEvaluable(Gate, "median duration", reason));
        }
        else
        {
            parts.Add(Validity(lines));
            parts.Add(Yield(lines, holdoutOrders.Count));
            parts.Add(Relevance(lines, judgments));
            parts.Add(Median(lines));
        }
        parts.Add(Overall(parts));

        figures.AddRange(Reported(lines, "held-out"));
        var dev = also.SelectMany(f => f.Lines)
            .Where(l => ResearchCorpus.SetOf(orders, l.Id) == ResearchCorpus.DevSet && l.InputHash == hashes[l.Id])
            .ToList();
        figures.Add(dev.Count == 0
            ? "development: no lines for the current input"
            : $"development (no verdict): {dev.Count} lines, {dev.Count(l => l.Entries.Count > 0)} with entries, {dev.Sum(l => l.Entries.Count)} entries, "
              + $"valid answers {dev.Count(RunStatus.ValidAnswer)}/{dev.Count(l => l.Condense is not null)}, median {Ms(dev.Where(RunStatus.HasLatency).Select(l => l.DurationMs))}");
        figures.AddRange(Reported(dev, "development"));
        return (parts, figures);
    }

    /// <summary>Exit code of the overall verdict: 0 passed, 1 failed, 4 not evaluable.</summary>
    public static int ExitCode(GateVerdict verdict) => verdict switch
    {
        GateVerdict.Passed => 0,
        GateVerdict.Failed => 1,
        _ => 4,
    };

    private static bool Rebuilds(ResearchRunLine line, ResearchOrder order, ResearchKit kit, RunSettingsRecord settings)
    {
        var pages = line.Pages.Select(p => new ResearchPage(p.Id, p.Topic, p.Url, p.Title, p.RawText)).ToList();
        var proposed = line.Condense!.Proposed.Select(p => new ProposedPassage(p.Topic, p.Page, p.Quote)).ToList();
        var (entries, _) = kit.Build(proposed, pages, order.Request, settings.MaxEntries, line.RetrievedAt);
        return entries.Count == line.Entries.Count && entries.Zip(line.Entries).All(p =>
            p.First.Id == p.Second.Id && p.First.Quote == p.Second.Quote && p.First.Url == p.Second.Url && p.First.Start == p.Second.Start
            && p.First.Length == p.Second.Length && string.Equals(p.First.RawSha256, p.Second.RawSha256, StringComparison.OrdinalIgnoreCase));
    }

    private static string? VerificationProblem(VerificationReport? report, IReadOnlyList<RunFile> files, int entries)
    {
        if (report is null)
            return "the independent check (research_verify.py) did not run or its report is missing";
        if (report.Tool != VerifierName)
            return $"the report is not from {VerifierName}";
        var expected = files.Select(f => f.Sha256.ToLowerInvariant()).Order(StringComparer.Ordinal).ToList();
        var read = report.Files.Select(f => f.Sha256.ToLowerInvariant()).Order(StringComparer.Ordinal).ToList();
        if (!expected.SequenceEqual(read, StringComparer.Ordinal))
            return "the independent check read other files or other contents than the ones evaluated here; run it again over exactly these files";
        return report.Entries != entries ? $"the independent check counted {report.Entries} entries, the files hold {entries}" : null;
    }

    private static GateResult K1(IReadOnlyList<string> own, VerificationReport? report, string? problem, IReadOnlyList<string> missingRaw, int entries)
    {
        var independent = report?.Violations ?? [];
        var detail = $"independent check {(report is null ? "missing" : $"{independent.Count} violations")}, own recheck {own.Count} violations over {entries} entries";
        if (independent.Count > 0 || own.Count > 0)
        {
            var examples = independent.Select(v => $"{v.Order}/{v.Entry}: {v.Reason}").Concat(own).Take(5);
            return new GateResult(Gate, "K1 word for word", GateVerdict.Failed, $"{detail}; halt, find the error in DossierBuilder, then measure VR completely again: {string.Join("; ", examples)}");
        }
        if (problem is not null)
            return GateResult.NotEvaluable(Gate, "K1 word for word", $"{detail}; {problem}");
        if (missingRaw.Count > 0)
            return GateResult.NotEvaluable(Gate, "K1 word for word", $"{detail}; raw texts missing");
        if (entries == 0)
            return GateResult.NotEvaluable(Gate, "K1 word for word", $"{detail}; no entries, nothing was checked");
        return new GateResult(Gate, "K1 word for word", GateVerdict.Passed, detail);
    }

    private static GateResult Validity(IReadOnlyList<ResearchRunLine> lines)
    {
        var called = lines.Where(l => l.Condense is not null).ToList();
        var valid = called.Count(RunStatus.ValidAnswer);
        return called.Count == 0
            ? GateResult.NotEvaluable(Gate, "validity", "no call of the passage picker")
            : GateResult.Of(Gate, "validity", true, valid >= MinValidity * called.Count, Invariant($"{valid}/{called.Count} answers usable ({Percent(valid, called.Count)}, at least {MinValidity:P0})"));
    }

    private static GateResult Yield(IReadOnlyList<ResearchRunLine> lines, int orders)
    {
        var found = lines.Count(l => l.Entries.Count > 0);
        return GateResult.Of(Gate, "yield", orders > 0, found >= MinYield * orders, Invariant($"{found}/{orders} held-out orders with at least one entry ({Percent(found, orders)}, at least {MinYield:P0})"));
    }

    private static GateResult Relevance(IReadOnlyList<ResearchRunLine> lines, IReadOnlyList<JudgeLine> judgments)
    {
        var helps = 0;
        var entries = 0;
        var unjudged = new List<string>();
        foreach (var line in lines.Where(l => l.Entries.Count > 0))
        {
            var judgement = judgments.LastOrDefault(j => j.Id == line.Id && j.Status == RelevanceJudge.Ok && j.RunHash == RelevanceJudge.RunHash(line) && j.InputHash == RelevanceJudge.InputHash(line));
            if (judgement is null || judgement.Verdicts.Count != line.Entries.Count)
            {
                unjudged.Add(line.Id);
                continue;
            }
            entries += line.Entries.Count;
            helps += judgement.Verdicts.Count(v => v.Helps);
        }
        if (unjudged.Count > 0)
            return GateResult.NotEvaluable(Gate, "relevance", $"no usable judgement for the current entries of {EvalBasics.Few(unjudged)}");
        return entries == 0
            ? GateResult.NotEvaluable(Gate, "relevance", "no held-out entry to judge")
            : GateResult.Of(Gate, "relevance", true, helps >= MinRelevance * entries, Invariant($"{helps}/{entries} entries help with their topic ({Percent(helps, entries)}, at least {MinRelevance:P0})"));
    }

    private static GateResult Median(IReadOnlyList<ResearchRunLine> lines)
    {
        var timed = lines.Where(RunStatus.HasLatency).Select(l => (double)l.DurationMs).Order().ToList();
        var fast = lines.Count - timed.Count;
        if (timed.Count == 0)
            return GateResult.NotEvaluable(Gate, "median duration", "no order with a duration of search and picking");
        var median = EvalBasics.Percentile(timed, 0.5);
        return GateResult.Of(Gate, "median duration", true, median <= MaxMedianMs,
            Invariant($"median {median / 1000:0.0} s over {timed.Count} orders (at most {MaxMedianMs / 1000} s; {fast} orders without such a duration left out: all topics withheld or a fast failure)"));
    }

    private static GateResult Overall(IReadOnlyList<GateResult> parts)
    {
        var verdict = parts.Any(p => p.Verdict == GateVerdict.Failed) ? GateVerdict.Failed
            : parts.Any(p => p.Verdict == GateVerdict.NotEvaluable) ? GateVerdict.NotEvaluable
            : GateVerdict.Passed;
        var open = parts.Where(p => p.Verdict != GateVerdict.Passed).Select(p => p.Scope).ToList();
        return new GateResult(Gate, "overall", verdict, open.Count == 0 ? "every part passed" : $"not passed: {string.Join(", ", open)}");
    }

    private static IEnumerable<string> Reported(IReadOnlyList<ResearchRunLine> lines, string scope)
    {
        if (lines.Count == 0)
            yield break;
        var statuses = lines.GroupBy(l => l.ErrorCode is null ? l.Status : $"{l.Status}/{l.ErrorCode}").Select(g => $"{g.Count()} {g.Key}");
        yield return $"{scope}: {string.Join(", ", statuses)}; topics sent {lines.Sum(l => l.Sent.Count)}, withheld {lines.Sum(l => l.Withheld)}, over the limit {lines.Sum(l => l.OverLimit)}";
        var searches = lines.SelectMany(l => l.Searches).ToList();
        yield return $"{scope}: {searches.Count} searches ({searches.Count(s => s.ErrorCode is not null)} failed), {lines.Sum(l => l.Pages.Count)} pages with raw text, "
            + $"search duration median {Ms(searches.Select(s => s.DurationMs))}, max {(searches.Count == 0 ? "–" : Invariant($"{searches.Max(s => s.DurationMs) / 1000.0:0.0} s"))}";
        var proposed = lines.Sum(l => l.Condense?.Proposed.Count ?? 0) + lines.Sum(l => l.Condense?.ParserDropped.Values.Sum() ?? 0);
        var dropped = lines.SelectMany(l => l.Dropped).GroupBy(d => d.Key).Select(g => $"{g.Key} {g.Sum(d => d.Value)} ({Percent(g.Sum(d => d.Value), proposed)})");
        yield return $"{scope}: {proposed} passages proposed, {lines.Sum(l => l.Entries.Count)} entries; dropped {string.Join(", ", dropped.DefaultIfEmpty("none"))}; models {string.Join(", ", lines.Select(l => l.Condense?.Model).OfType<string>().Distinct().DefaultIfEmpty("–"))}";
    }

    private static string Ms(IEnumerable<int> values)
    {
        var sorted = values.Select(v => (double)v).Order().ToList();
        return sorted.Count == 0 ? "–" : Invariant($"{EvalBasics.Percentile(sorted, 0.5) / 1000:0.0} s");
    }

    private static string Percent(int part, int whole) => whole == 0 ? "–" : (part / (double)whole).ToString("P1", CultureInfo.InvariantCulture);
}
