using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Persistence.Profiles;
using Geef.Atelier.Infrastructure.Intake;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Intake;

/// <summary>
/// Replays the stored raw clarifier answers of <c>tools/IntakeEval</c> (<c>Intake/Corpus/results-*.jsonl</c>) through the parser
/// and the gate without a model call and computes the acceptance figures of contract stage 2, 6.6. Only the constructive gate
/// figures (d, at most three questions) are asserted here; the thresholds of the other gates are decided on the held-out set
/// and the second corpus by the person who switches the intake on, from the printed figures.
/// </summary>
public sealed class IntakeCorpusTests(ITestOutputHelper output)
{
    public const string MainCorpus = "intake-corpus.jsonl";
    public const string SecondCorpus = "intake-corpus-second.jsonl";

    public sealed record ProfileItem(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("organization")] string? Organization,
        [property: JsonPropertyName("formality")] string? Formality,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("signature")] string? Signature);

    public sealed record FileItem([property: JsonPropertyName("name")] string Name);

    public sealed record Item(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("corpus")] string Corpus,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("lang")] string Lang,
        [property: JsonPropertyName("request")] string Request,
        [property: JsonPropertyName("files")] IReadOnlyList<FileItem>? Files,
        [property: JsonPropertyName("profile")] ProfileItem? Profile,
        [property: JsonPropertyName("labels")] IReadOnlyDictionary<string, string>? Labels);

    public sealed record Result(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("duration_ms")] int DurationMs,
        [property: JsonPropertyName("raw")] string? Raw);

    public sealed record Replay(Item Item, Result Result, ClarifierOutput? Output, QuestionGateResult? Gate);

    public sealed record Figures(
        int Entries,
        double MedianMs,
        double P90Ms,
        double MeanQuestions,
        int MaxQuestions,
        double CompleteAskedRate,
        int CompleteMaxQuestions,
        int BadDeliveredQuestions,
        double RawQuestionDropRate,
        double ForbiddenAskRate,
        int ResearchableTopicAsked,
        int ProfileTopicAsked,
        double NecessaryHitRate,
        double FailureRate);

    private sealed class NoProfiles : IUserProfileRepository
    {
        public Task<UserProfileEntity?> GetAsync(string username, CancellationToken ct = default) => Task.FromResult<UserProfileEntity?>(null);
        public Task UpsertAsync(UserProfileEntity profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string username, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static readonly ProfileMemoryResolution Memory = new(new NoProfiles());

    private static readonly HashSet<GateDecision> FormDrops =
        [GateDecision.DroppedTooLong, GateDecision.DroppedBadOptions, GateDecision.DroppedJargon, GateDecision.DroppedLanguageMismatch];

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

    public static IReadOnlyList<T> LoadJsonLines<T>(string path) =>
        File.Exists(path)
            ? File.ReadLines(path).Where(l => l.TrimStart().StartsWith('{')).Select(l => JsonSerializer.Deserialize<T>(l)!).ToList()
            : [];

    public static bool InDevelopmentSet(string request) => SHA256.HashData(Encoding.UTF8.GetBytes(request))[0] % 10 < 7;

    public static Replay ReplayOne(Item item, Result result)
    {
        var output = result.Raw is null ? null : ClarifierOutputParser.Parse(result.Raw);
        if (output is null)
            return new Replay(item, result, null, null);

        var profile = item.Profile is not { } p ? null : new UserProfileEntity
        {
            Username = "corpus", DisplayName = p.DisplayName, Role = p.Role, Organization = p.Organization,
            Formality = Enum.TryParse<Formality>(p.Formality, true, out var formality) ? formality : Formality.Unset,
            Language = p.Language, Signature = p.Signature, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch,
        };
        var known = Memory.Resolve(new MemoryFacts(profile), output.TextType);
        var files = (item.Files ?? []).Select((f, i) => new RevisionCandidate(i + 1, Guid.Empty, f.Name)).ToList();
        var revision = RevisionDetector.Choose(item.Request, files, output.RevisionFileNumber, output.IsRevision);
        var gate = QuestionGate.Apply(new QuestionGateInput(output, item.Request, files.Count > 0, revision is not null, known, 3));
        return new Replay(item, result, output, gate);
    }

    public static Figures Measure(IReadOnlyList<Replay> replays)
    {
        var durations = replays.Select(r => (double)r.Result.DurationMs).Order().ToList();
        var counts = replays.Select(r => r.Gate?.Questions.Count ?? 0).ToList();
        var complete = replays.Where(r => r.Item.Category == "complete").ToList();
        var questions = replays.SelectMany(r => r.Gate?.Questions ?? []).ToList();
        var rawAsks = replays.Sum(r => r.Output?.Gaps.Count(g => g.Resolution == GapResolution.Ask && g.Topic != GapTopic.Length) ?? 0);
        var formDrops = replays.Sum(r => r.Gate?.Trace.Count(t => FormDrops.Contains(t.Decision)) ?? 0);
        var labelled = replays.Where(r => r.Item.Labels is { Count: > 0 }).ToList();

        int AskedWithLabel(Replay r, string level) =>
            (r.Gate?.Questions ?? []).Count(q => Label(r.Item, q.Slot) == level);

        var necessary = labelled.Sum(r => r.Item.Labels!.Count(l => l.Value == "necessary_ask"));
        var necessaryHits = labelled.Sum(r => r.Item.Labels!
            .Where(l => l.Value == "necessary_ask")
            .Count(l => (r.Gate?.Questions ?? []).Any(q => Topic(l.Key) == q.Slot)));

        return new Figures(
            replays.Count,
            Percentile(durations, 0.5),
            Percentile(durations, 0.9),
            counts.Count == 0 ? 0 : counts.Average(),
            counts.DefaultIfEmpty(0).Max(),
            complete.Count == 0 ? 0 : (double)complete.Count(r => (r.Gate?.Questions.Count ?? 0) > 0) / complete.Count,
            complete.Select(r => r.Gate?.Questions.Count ?? 0).DefaultIfEmpty(0).Max(),
            questions.Count(Bad),
            rawAsks == 0 ? 0 : (double)formDrops / rawAsks,
            labelled.Count == 0 ? 0 : (double)labelled.Count(r => AskedWithLabel(r, "forbidden_ask") > 0) / labelled.Count,
            labelled.Where(r => r.Item.Category == "researchable").Sum(r => AskedWithLabel(r, "forbidden_ask")),
            labelled.Where(r => r.Item.Category == "profile_covered").Sum(r => AskedWithLabel(r, "forbidden_ask")),
            necessary == 0 ? 1 : (double)necessaryHits / necessary,
            replays.Count == 0 ? 0 : (double)replays.Count(r => r.Result.Status != nameof(IntakeRecordStatus.Ok)) / replays.Count);
    }

    private static bool Bad(ClarifyQuestion q) =>
        q.Text.Length is 0 or > QuestionGate.MaxQuestionChars
        || q.Options.Count is < QuestionGate.MinOptions or > QuestionGate.MaxOptions
        || q.Options.Any(o => o.Label.Length is 0 or > QuestionGate.MaxOptionChars)
        || JargonBlocklist.Contains(q.Text) || q.Options.Any(o => JargonBlocklist.Contains(o.Label));

    private static GapTopic? Topic(string key) => Enum.TryParse<GapTopic>(key.Replace("_", string.Empty, StringComparison.Ordinal), true, out var t) ? t : null;

    private static string? Label(Item item, GapTopic topic) =>
        item.Labels?.FirstOrDefault(l => Topic(l.Key) == topic).Value;

    private static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0)
            return 0;
        var rank = p * (sorted.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }

    [Fact]
    public void Stored_results_keep_the_question_rule_after_replay()
    {
        var directory = CorpusDirectory();
        var items = LoadJsonLines<Item>(Path.Combine(directory, MainCorpus))
            .Concat(LoadJsonLines<Item>(Path.Combine(directory, SecondCorpus)))
            .ToDictionary(i => i.Id, StringComparer.Ordinal);
        var resultFiles = Directory.Exists(directory) ? Directory.GetFiles(directory, "results-*.jsonl").Order().ToList() : [];
        if (items.Count == 0 || resultFiles.Count == 0)
        {
            output.WriteLine($"No corpus or results yet in {directory}; nothing to replay.");
            return;
        }

        foreach (var file in resultFiles)
        {
            var replays = LoadJsonLines<Result>(file)
                .GroupBy(r => r.Id).Select(g => g.Last())
                .Where(r => items.ContainsKey(r.Id))
                .Select(r => ReplayOne(items[r.Id], r))
                .ToList();

            foreach (var (name, subset) in new[]
            {
                ("all", replays),
                ("development", replays.Where(r => r.Item.Corpus == "main" && InDevelopmentSet(r.Item.Request)).ToList()),
                ("held-out", replays.Where(r => r.Item.Corpus == "main" && !InDevelopmentSet(r.Item.Request)).ToList()),
                ("second", replays.Where(r => r.Item.Corpus == "second").ToList()),
            })
            {
                var figures = Measure(subset);
                output.WriteLine($"{Path.GetFileName(file)} [{name}] {figures}");
                Assert.Equal(0, figures.BadDeliveredQuestions);
                Assert.InRange(figures.MaxQuestions, 0, QuestionGate.HardMaxQuestions);
            }
        }
    }

    [Fact]
    public void The_figures_follow_the_definitions_of_the_gates()
    {
        static Item Item(string id, string category, IReadOnlyDictionary<string, string>? labels = null) =>
            new(id, "main", category, "de", $"Schreib einen Text {id}.", null, null, labels);
        static string Raw(params string[] gaps) =>
            $$"""{"text_type":"Brief","language":"de","understanding":"Ein Brief.","is_revision":false,"length_scale":"standard","gaps":[{{string.Join(',', gaps)}}],"research_topics":[]}""";
        const string AskAudience = """{"topic":"audience","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Kunden","confidence":"unsure","question":{"text":"Wer liest den Brief?","options":["Kunden","Kollegen"]}}""";
        const string AskJargon = """{"topic":"purpose","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"x","confidence":"unsure","question":{"text":"Was ist der Call to Action?","options":["Kaufen","Anrufen"]}}""";
        const string AskLength = """{"topic":"length","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"kurz","confidence":"unsure","question":null}""";

        var replays = new[]
        {
            ReplayOne(Item("c1", "complete", new Dictionary<string, string> { ["audience"] = "forbidden_ask" }), new Result("c1", "Ok", 10_000, Raw(AskAudience))),
            ReplayOne(Item("c2", "complete"), new Result("c2", "Ok", 20_000, Raw())),
            ReplayOne(Item("s1", "sparse", new Dictionary<string, string> { ["length"] = "necessary_ask", ["tone"] = "necessary_ask" }), new Result("s1", "Ok", 30_000, Raw(AskLength, AskJargon))),
            ReplayOne(Item("f1", "sparse"), new Result("f1", "TimedOut", 40_000, null)),
        };

        var figures = Measure(replays);

        Assert.Equal(4, figures.Entries);
        Assert.Equal(25_000, figures.MedianMs);
        Assert.Equal(37_000, figures.P90Ms, 3);
        Assert.Equal(0.5, figures.MeanQuestions);
        Assert.Equal(1, figures.MaxQuestions);
        Assert.Equal(0.5, figures.CompleteAskedRate);
        Assert.Equal(1, figures.CompleteMaxQuestions);
        Assert.Equal(0, figures.BadDeliveredQuestions);
        Assert.Equal(0.5, figures.RawQuestionDropRate);
        Assert.Equal(0.5, figures.ForbiddenAskRate);
        Assert.Equal(0.5, figures.NecessaryHitRate);
        Assert.Equal(0.25, figures.FailureRate);
    }
}
