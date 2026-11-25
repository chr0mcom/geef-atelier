using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Tools.IntakeEval;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Intake;

/// <summary>
/// Replays the stored raw clarifier answers of <c>tools/IntakeEval</c> (<c>Intake/Corpus/results-*.jsonl</c>) through the parser and
/// the gate without a model call, with the evaluation code of the tool (<see cref="CorpusEvaluation"/>). As soon as a result file
/// exists, the data must be complete (all entries of both corpora answered, labelled, contract sizes, held-out and second part not
/// empty), otherwise the test fails; gate d and the question limit are asserted, the other gates are printed as passed, failed or
/// not evaluable for the person who switches the intake on. The other tests pin every way a gate could pass without data.
/// </summary>
public sealed class IntakeCorpusTests(ITestOutputHelper output)
{
    public const string MainCorpus = "intake-corpus.jsonl";
    public const string SecondCorpus = "intake-corpus-second.jsonl";

    private static readonly TimeSpan Limit = new IntakeOptions().Timeout;

    private const string NoGaps =
        """{"text_type":"Brief","language":"de","understanding":"Ein Brief.","is_revision":false,"length_scale":"standard","gaps":[],"research_topics":[]}""";

    private const string AskLength =
        """{"text_type":"Brief","language":"de","understanding":"Ein Brief.","is_revision":false,"length_scale":"standard","gaps":[{"topic":"length","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"kurz","confidence":"unsure","question":null}],"research_topics":[]}""";

    public static string CorpusDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Geef.Atelier.Tests", "Intake", "Corpus");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return Path.Combine(AppContext.BaseDirectory, "Intake", "Corpus");
    }

    [Fact]
    public void Stored_results_are_complete_keep_the_question_rule_and_get_a_verdict_for_every_gate()
    {
        var directory = CorpusDirectory();
        var corpus = new[] { MainCorpus, SecondCorpus }
            .Select(name => Path.Combine(directory, name))
            .Where(File.Exists)
            .SelectMany(Corpus.Load)
            .ToList();
        var resultFiles = Directory.Exists(directory) ? Directory.GetFiles(directory, "results-*.jsonl").Order().ToList() : [];
        if (resultFiles.Count == 0)
        {
            var verdicts = CorpusEvaluation.Verdicts(corpus, [], Limit);
            output.WriteLine($"No results yet in {directory}; every gate is not evaluable.");
            Assert.All(verdicts, v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
            return;
        }

        foreach (var file in resultFiles)
        {
            var lines = Results.Load(file);
            var problems = CorpusEvaluation.Problems(corpus, lines);
            Assert.True(problems.Count == 0, $"{Path.GetFileName(file)} cannot be judged: {string.Join("; ", problems)}");

            foreach (var verdict in CorpusEvaluation.Verdicts(corpus, lines, Limit))
                output.WriteLine($"{Path.GetFileName(file)} {verdict}");

            var byId = corpus.ToDictionary(e => e.Id, StringComparer.Ordinal);
            var answered = CorpusEvaluation.LastAnswers(lines.Select(l => CorpusEvaluation.ReplayOne(byId[l.Id], l)).ToList(), Limit);
            var figures = CorpusEvaluation.Questions(answered);
            Assert.Equal(0, figures.BadDeliveredQuestions);
            Assert.InRange(figures.MaxQuestions, 0, QuestionGate.HardMaxQuestions);
        }
    }

    private static readonly (string Corpus, string Category, int Count)[] Plan =
    [
        ("main", "sparse", 50), ("main", "complete", 40), ("main", "revision", 10), ("main", "researchable", 10), ("main", "profile_covered", 10),
        ("second", "sparse", 16), ("second", "complete", 10), ("second", "revision", 5), ("second", "researchable", 5), ("second", "profile_covered", 4),
    ];

    private static IReadOnlyDictionary<string, string> LabelsFor(string category) => category switch
    {
        "sparse" => new Dictionary<string, string> { ["length"] = "necessary_ask" },
        "researchable" => new Dictionary<string, string> { ["facts"] = "forbidden_ask" },
        "profile_covered" => new Dictionary<string, string> { ["sender"] = "forbidden_ask" },
        _ => new Dictionary<string, string> { ["audience"] = "acceptable_ask" },
    };

    internal static List<CorpusEntry> FullCorpus(Func<string, bool>? acceptRequest = null)
    {
        var entries = new List<CorpusEntry>();
        var number = 0;
        foreach (var (corpus, category, count) in Plan)
        {
            for (var i = 0; i < count; i++)
            {
                string request;
                do
                {
                    request = $"Schreib einen Text für den Verein, Fall {++number}.";
                }
                while (corpus == "main" && acceptRequest is not null && !acceptRequest(request));
                var index = entries.Count(e => e.Corpus == corpus) + 1;
                entries.Add(new CorpusEntry($"{corpus}-{index:000}", corpus, category, "de", request, null, null, LabelsFor(category)));
            }
        }
        return entries;
    }

    private static EvalResult Line(CorpusEntry entry, string status = "Ok", int durationMs = 10_000, string? raw = "") =>
        new(entry.Id, entry.Corpus, entry.Category, entry.Lang, entry.Split(), status, "model", durationMs, null,
            raw == string.Empty ? (entry.Category == "sparse" ? AskLength : NoGaps) : raw,
            0, [], [], [], DateTimeOffset.UnixEpoch, 40_000, CorpusEvaluation.InputHash(entry));

    private static List<EvalResult> GoodLines(IReadOnlyList<CorpusEntry> corpus) => corpus.Select(e => Line(e)).ToList();

    private static GateResult Verdict(IReadOnlyList<GateResult> verdicts, string gate, string scope) =>
        Assert.Single(verdicts, v => v.Gate == gate && v.Scope == scope);

    [Fact]
    public void Complete_good_data_passes_every_gate()
    {
        var corpus = FullCorpus();

        var verdicts = CorpusEvaluation.Verdicts(corpus, GoodLines(corpus), Limit);

        Assert.Equal(14, verdicts.Count);
        Assert.All(verdicts, v => Assert.True(v.Verdict == GateVerdict.Passed, v.ToString()));
    }

    [Fact]
    public void Repeated_calls_count_in_latency_and_failures_and_are_reported()
    {
        var corpus = FullCorpus();
        var second = corpus.Where(e => e.Corpus == "second").Take(4).ToList();
        var lines = second.Select(e => Line(e, "TimedOut", 40_000, null)).Concat(GoodLines(corpus)).ToList();

        var verdicts = CorpusEvaluation.Verdicts(corpus, lines, Limit);

        var f = Verdict(verdicts, "f", CorpusEvaluation.Second);
        Assert.Equal(GateVerdict.Failed, f.Verdict);
        Assert.Contains("44 calls for 40 entries (4 repeated)", f.Detail, StringComparison.Ordinal);
        Assert.Equal(GateVerdict.Passed, Verdict(verdicts, "b", CorpusEvaluation.Second).Verdict);
        var calls = CorpusEvaluation.Calls(lines.Where(l => l.Corpus == "second").Select(l => CorpusEvaluation.ReplayOne(corpus.Single(e => e.Id == l.Id), l)).ToList(), Limit);
        Assert.Equal((44, 4, 4), (calls.Calls, calls.Retries, calls.Failures));
    }

    [Fact]
    public void Slow_failed_attempts_stay_in_the_latency()
    {
        var corpus = FullCorpus();
        var lines = corpus.Where(e => e.Corpus == "second").Select(e => Line(e, "TimedOut", 45_000, null)).Concat(GoodLines(corpus)).ToList();

        var verdicts = CorpusEvaluation.Verdicts(corpus, lines, Limit);

        Assert.Equal(GateVerdict.Failed, Verdict(verdicts, "a", CorpusEvaluation.Second).Verdict);
    }

    [Fact]
    public void An_ok_answer_over_the_production_limit_is_a_failure_and_no_answer()
    {
        var corpus = FullCorpus();
        var slow = corpus.Where(e => e.Corpus == "second").Take(3).Select(e => e.Id).ToHashSet();
        var lines = corpus.Select(e => slow.Contains(e.Id) ? Line(e, durationMs: 45_000) : Line(e)).ToList();

        var verdicts = CorpusEvaluation.Verdicts(corpus, lines, Limit);

        Assert.Equal(GateVerdict.Failed, Verdict(verdicts, "f", CorpusEvaluation.Second).Verdict);
        Assert.Contains("3 over the production limit", Verdict(verdicts, "f", CorpusEvaluation.Second).Detail, StringComparison.Ordinal);
        Assert.Contains("over 37 answered entries", Verdict(verdicts, "b", CorpusEvaluation.Second).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Ok_answers_that_cannot_be_replayed_are_failures_and_never_count_as_no_question()
    {
        var corpus = FullCorpus();
        var lines = corpus.Select(e => Line(e, raw: "not json")).ToList();

        var verdicts = CorpusEvaluation.Verdicts(corpus, lines, Limit);

        foreach (var scope in new[] { CorpusEvaluation.HeldOut, CorpusEvaluation.Second })
        {
            Assert.Equal(GateVerdict.Failed, Verdict(verdicts, "f", scope).Verdict);
            foreach (var gate in new[] { "b", "c", "d", "e", "e'" })
                Assert.Equal(GateVerdict.NotEvaluable, Verdict(verdicts, gate, scope).Verdict);
        }
    }

    [Fact]
    public void Only_failed_calls_pass_no_gate()
    {
        var corpus = FullCorpus();
        var lines = corpus.Select(e => Line(e, "TimedOut", 40_000, null)).ToList();

        var verdicts = CorpusEvaluation.Verdicts(corpus, lines, Limit);

        Assert.DoesNotContain(verdicts, v => v.Verdict == GateVerdict.Passed);
    }

    public static TheoryData<string> BrokenData() =>
    [
        "missing result", "unknown result", "too few main entries", "too few second entries", "unlabelled", "no second corpus", "empty held-out",
        "no input hash", "other request", "other profile", "other category", "other language",
    ];

    [Theory]
    [MemberData(nameof(BrokenData))]
    public void Incomplete_data_makes_every_gate_not_evaluable(string broken)
    {
        var corpus = broken == "empty held-out" ? FullCorpus(Corpus.InDevelopmentSet) : FullCorpus();
        var lines = GoodLines(corpus);
        switch (broken)
        {
            case "missing result":
                lines.RemoveAt(7);
                break;
            case "unknown result":
                lines.Add(Line(corpus[0]) with { Id = "main-999" });
                break;
            case "too few main entries":
                corpus.RemoveAt(0);
                lines.RemoveAt(0);
                break;
            case "too few second entries":
                corpus.RemoveAt(corpus.Count - 1);
                lines.RemoveAt(lines.Count - 1);
                break;
            case "unlabelled":
                corpus[3] = corpus[3] with { Labels = null };
                break;
            case "no second corpus":
                corpus.RemoveAll(e => e.Corpus == "second");
                lines.RemoveAll(l => l.Corpus == "second");
                break;
            case "no input hash":
                lines[5] = lines[5] with { InputHash = null };
                break;
            case "other request":
                corpus[5] = corpus[5] with { Request = corpus[5].Request + " Bitte freundlich." };
                break;
            case "other profile":
                corpus[5] = corpus[5] with { Profile = new CorpusProfile("Alex Muster", null, null, null, null, null, null, null) };
                break;
            case "other category":
                lines[5] = lines[5] with { Category = "researchable" };
                break;
            case "other language":
                lines[5] = lines[5] with { Lang = "en" };
                break;
        }

        var problems = CorpusEvaluation.Problems(corpus, lines);
        var verdicts = CorpusEvaluation.Verdicts(corpus, lines, Limit);

        Assert.NotEmpty(problems);
        Assert.Equal(14, verdicts.Count);
        Assert.All(verdicts, v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
    }

    [Fact]
    public void An_empty_part_passes_no_gate()
    {
        var verdicts = CorpusEvaluation.Judge("empty", [], [], Limit);

        Assert.All(verdicts, v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
    }

    [Fact]
    public void Gate_e_needs_an_answer_for_the_researchable_entries_of_its_part()
    {
        var corpus = FullCorpus().Where(e => e.Corpus == "second").ToList();
        var lines = corpus.Select(e => e.Category == "researchable" ? Line(e, "Failed", 1_000, null) : Line(e))
            .Select(l => CorpusEvaluation.ReplayOne(corpus.Single(e => e.Id == l.Id), l)).ToList();

        var e = Assert.Single(CorpusEvaluation.Judge(CorpusEvaluation.Second, corpus, lines, Limit), v => v.Gate == "e");

        Assert.Equal(GateVerdict.NotEvaluable, e.Verdict);
    }

    [Fact]
    public void The_figures_follow_the_definitions_of_the_gates()
    {
        static CorpusEntry Entry(string id, string category, IReadOnlyDictionary<string, string>? labels = null) =>
            new(id, "main", category, "de", $"Schreib einen Text {id}.", null, null, labels);
        static string Raw(params string[] gaps) =>
            $$"""{"text_type":"Brief","language":"de","understanding":"Ein Brief.","is_revision":false,"length_scale":"standard","gaps":[{{string.Join(',', gaps)}}],"research_topics":[]}""";
        const string Audience = """{"topic":"audience","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Kunden","confidence":"unsure","question":{"text":"Wer liest den Brief?","options":["Kunden","Kollegen"]}}""";
        const string Jargon = """{"topic":"purpose","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"x","confidence":"unsure","question":{"text":"Was ist der Call to Action?","options":["Kaufen","Anrufen"]}}""";
        const string Length = """{"topic":"length","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"kurz","confidence":"unsure","question":null}""";

        var c1 = Entry("c1", "complete", new Dictionary<string, string> { ["audience"] = "forbidden_ask" });
        var c2 = Entry("c2", "complete");
        var s1 = Entry("s1", "sparse", new Dictionary<string, string> { ["length"] = "necessary_ask", ["tone"] = "necessary_ask" });
        var f1 = Entry("f1", "sparse");
        EvalResult L(CorpusEntry e, string status, int ms, string? raw) =>
            new(e.Id, "main", e.Category, "de", "dev", status, "m", ms, null, raw, 0, [], [], [], DateTimeOffset.UnixEpoch, 40_000);
        var lines = new[]
        {
            CorpusEvaluation.ReplayOne(c1, L(c1, "Ok", 10_000, Raw(Audience))),
            CorpusEvaluation.ReplayOne(c2, L(c2, "Ok", 20_000, Raw())),
            CorpusEvaluation.ReplayOne(s1, L(s1, "Ok", 30_000, Raw(Length, Jargon))),
            CorpusEvaluation.ReplayOne(f1, L(f1, "TimedOut", 40_000, null)),
        };

        var calls = CorpusEvaluation.Calls(lines, Limit);
        var questions = CorpusEvaluation.Questions(CorpusEvaluation.LastAnswers(lines, Limit));

        Assert.Equal((4, 4, 0, 1), (calls.Calls, calls.Entries, calls.Retries, calls.Failures));
        Assert.Equal(25_000, calls.MedianMs);
        Assert.Equal(37_000, calls.P90Ms, 3);
        Assert.Equal(0.25, calls.FailureRate);
        Assert.Equal(3, questions.Answered);
        Assert.Equal(2.0 / 3, questions.MeanQuestions, 6);
        Assert.Equal(1, questions.MaxQuestions);
        Assert.Equal(0.5, questions.CompleteAskedRate);
        Assert.Equal(1, questions.CompleteMaxQuestions);
        Assert.Equal(0, questions.BadDeliveredQuestions);
        Assert.Equal(0.5, questions.RawQuestionDropRate);
        Assert.Equal(0.5, questions.ForbiddenAskRate);
        Assert.Equal(0.5, questions.NecessaryHitRate);
    }

    [Fact]
    public void Results_of_another_corpus_with_the_same_ids_are_not_evaluable_and_are_called_again()
    {
        var corpusA = FullCorpus();
        var corpusB = corpusA.Select(e => e with { Request = e.Request.Replace("Verein", "Chor", StringComparison.Ordinal) }).ToList();
        var lines = GoodLines(corpusA);

        Assert.All(CorpusEvaluation.Verdicts(corpusB, lines, Limit), v => Assert.Equal(GateVerdict.NotEvaluable, v.Verdict));
        Assert.Contains(CorpusEvaluation.Problems(corpusB, lines), p => p.Contains("another input", StringComparison.Ordinal));
        Assert.Equal(corpusA.Count, CorpusEvaluation.OtherInput(corpusB, lines).Count);
        Assert.Empty(CorpusEvaluation.Answered(corpusB, lines));
        Assert.Equal(corpusA.Count, CorpusEvaluation.Answered(corpusA, lines).Count);
    }

    [Fact]
    public void The_input_hash_follows_request_files_and_profile()
    {
        var entry = new CorpusEntry("main-001", "main", "sparse", "de", "Schreib eine Einladung.", null, null, null);
        var withFile = entry with { Files = [new CorpusFile("notiz.txt", "text", "Das Fest beginnt um 18 Uhr.", null)] };
        var withProfile = entry with { Profile = new CorpusProfile("Alex Muster", null, null, null, "de", null, null, null) };

        var hashes = new[] { entry, withFile, withProfile, entry with { Request = "Schreib eine Absage." } }.Select(CorpusEvaluation.InputHash).ToList();

        Assert.Equal(CorpusEvaluation.InputHash(entry), CorpusEvaluation.InputHash(entry with { Labels = new Dictionary<string, string> { ["length"] = "necessary_ask" } }));
        Assert.Equal(4, hashes.Distinct(StringComparer.Ordinal).Count());
        Assert.Matches("^[0-9a-f]{64}$", hashes[0]);
    }

    [Fact]
    public void Only_the_last_result_for_the_current_input_decides_whether_run_calls_an_entry_again()
    {
        var corpus = FullCorpus();
        var entry = corpus[0];
        var stale = Line(entry) with { InputHash = new string('0', 64) };

        Assert.DoesNotContain(entry.Id, CorpusEvaluation.Answered(corpus, [stale]));
        Assert.Contains(entry.Id, CorpusEvaluation.Answered(corpus, [stale, Line(entry)]));
        Assert.Contains(entry.Id, CorpusEvaluation.Answered(corpus, [Line(entry, "Invalid", 5_000, null)]));
        Assert.DoesNotContain(entry.Id, CorpusEvaluation.Answered(corpus, [Line(entry), Line(entry, "TimedOut", 40_000, null)]));
        Assert.Contains(entry.Id, CorpusEvaluation.Answered(corpus, [Line(entry), stale]));
    }

    [Fact]
    public void Fast_failures_never_improve_gate_a()
    {
        var corpus = FullCorpus();
        var slow = corpus.Select(e => Line(e, durationMs: 30_000)).ToList();
        var withFastFailures = corpus.SelectMany(e => new[] { Line(e, "Failed", 200, null), Line(e, "Failed", 200, null) }).Concat(slow).ToList();

        var before = CorpusEvaluation.Verdicts(corpus, slow, Limit);
        var after = CorpusEvaluation.Verdicts(corpus, withFastFailures, Limit);

        foreach (var scope in new[] { CorpusEvaluation.HeldOut, CorpusEvaluation.Second })
        {
            Assert.Equal(GateVerdict.Failed, Verdict(before, "a", scope).Verdict);
            Assert.Equal(GateVerdict.Failed, Verdict(after, "a", scope).Verdict);
            Assert.Equal(GateVerdict.Failed, Verdict(after, "f", scope).Verdict);
        }
    }

    [Fact]
    public void The_latency_counts_answers_and_time_outs_but_no_other_failure()
    {
        var entry = new CorpusEntry("main-001", "main", "sparse", "de", "Schreib einen Text.", null, null, null);
        EvalResult L(string status, int ms, string? raw) =>
            new(entry.Id, "main", "sparse", "de", "dev", status, "m", ms, null, raw, 0, [], [], [], DateTimeOffset.UnixEpoch, 40_000);
        var lines = new[]
        {
            CorpusEvaluation.ReplayOne(entry, L("Ok", 10_000, NoGaps)),
            CorpusEvaluation.ReplayOne(entry, L("TimedOut", 40_000, null)),
            CorpusEvaluation.ReplayOne(entry, L("Failed", 100, null)),
            CorpusEvaluation.ReplayOne(entry, L("Invalid", 300, null)),
            CorpusEvaluation.ReplayOne(entry, L("QueueTimeout", 50, null)),
        };

        var calls = CorpusEvaluation.Calls(lines, Limit);

        Assert.Equal((5, 2, 4), (calls.Calls, calls.Timed, calls.Failures));
        Assert.Equal(25_000, calls.MedianMs);
    }

    [Fact]
    public void Only_failed_calls_without_time_outs_leave_gate_a_not_evaluable()
    {
        var corpus = FullCorpus();
        var lines = corpus.Select(e => Line(e, "Failed", 200, null)).ToList();

        var verdicts = CorpusEvaluation.Verdicts(corpus, lines, Limit);

        Assert.Equal(GateVerdict.NotEvaluable, Verdict(verdicts, "a", CorpusEvaluation.Second).Verdict);
        Assert.Equal(GateVerdict.Failed, Verdict(verdicts, "f", CorpusEvaluation.Second).Verdict);
    }

    [Fact]
    public void The_help_offers_no_production_limit_for_shadow_because_it_has_none()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "tools", "IntakeEval", "Program.cs"));
        var shadowUsage = program.Split('\n').Single(l => l.TrimStart().StartsWith("IntakeEval shadow", StringComparison.Ordinal));

        Assert.DoesNotContain("--production-timeout-seconds", shadowUsage, StringComparison.Ordinal);
        Assert.Contains("--production-timeout-seconds", program, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tools", "IntakeEval", "Program.cs")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException("tools/IntakeEval/Program.cs not found above the test output.");
    }
}
