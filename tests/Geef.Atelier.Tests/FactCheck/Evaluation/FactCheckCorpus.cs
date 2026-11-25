using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Profiles;

namespace Geef.Atelier.Tools.FactCheckEval;

/// <summary>A file of a basis; <c>S0</c> is the text to revise of a revision, <c>S1…</c> are the attached files.</summary>
public sealed record CorpusFile(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("text")] string Text);

/// <summary>The saved profile of the person of a basis (only the fields that reach "About the person").</summary>
public sealed record CorpusProfile(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("organization")] string? Organization,
    [property: JsonPropertyName("signature")] string? Signature,
    [property: JsonPropertyName("no_gos")] string? NoGos);

/// <summary>A mistake in the material itself (question c): a wrong total, two passages that contradict each other or a replaced statement.</summary>
public sealed record SourceError(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("quote")] string Quote,
    [property: JsonPropertyName("quote_2")] string? Quote2,
    [property: JsonPropertyName("stated")] string? Stated);

/// <summary>A statement of the clean text that the independent labeller found not backed by the material.</summary>
public sealed record CorpusLabel(
    [property: JsonPropertyName("quote")] string Quote,
    [property: JsonPropertyName("why")] string? Why);

/// <summary>A planted mistake: its kind and the exact wording of the copy that carries it.</summary>
public sealed record CorpusPlant(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("inserted")] string Inserted);

/// <summary>
/// One basis of the fact-check corpus (written by <c>tools/Eval/geef_eval/factcheck_corpus.py</c>): request, answers, profile and
/// files, a clean text written only from them, the labels of the independent labeller and a copy with planted mistakes.
/// </summary>
public sealed record CorpusBasis(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("size")] string Size,
    [property: JsonPropertyName("lang")] string Lang,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("case")] string? Case,
    [property: JsonPropertyName("request")] string Request,
    [property: JsonPropertyName("answers")] IReadOnlyList<string>? Answers,
    [property: JsonPropertyName("profile")] CorpusProfile? Profile,
    [property: JsonPropertyName("files")] IReadOnlyList<CorpusFile>? Files,
    [property: JsonPropertyName("source_errors")] IReadOnlyList<SourceError>? SourceErrors,
    [property: JsonPropertyName("clean_text")] string CleanText,
    [property: JsonPropertyName("labels")] IReadOnlyList<CorpusLabel>? Labels,
    [property: JsonPropertyName("planted_text")] string? PlantedText,
    [property: JsonPropertyName("plants")] IReadOnlyList<CorpusPlant>? Plants);

/// <summary>A mistake the fact check should find in an item: the wording in the text and the question it belongs to (a, b, superseded, c).</summary>
public sealed record ExpectedError(string Kind, string Question, string Wording);

/// <summary>One text the fact check is called for: the clean text (<c>base</c>) or the planted copy (<c>planted</c>) of a basis.</summary>
public sealed record FactCheckItem(CorpusBasis Basis, string Variant, string Text, IReadOnlyList<ExpectedError> Errors)
{
    /// <summary>Id in the result file: <c>&lt;basis&gt;/&lt;variant&gt;</c>.</summary>
    public string Id => $"{Basis.Id}/{Variant}";

    /// <summary>Split of the basis; the parents' evening is always in the development set.</summary>
    public string Split => FactCheckCorpus.SplitOf(Basis);

    /// <summary>A text with at least one expected mistake of question a, b or superseded (labelled or planted).</summary>
    public bool IsFaulty => Errors.Any(e => e.Question != FactCheckCorpus.QuestionC);

    /// <summary>A text without any expected mistake of question a, b or superseded.</summary>
    public bool IsClean => !IsFaulty;

    /// <summary>The material of the basis contains a mistake of its own (question c).</summary>
    public bool HasFaultySource => Basis.Kind == FactCheckCorpus.FaultySourceKind;

    /// <summary>The material as the real direct path builds it: request, answers of the person, profile block and all files in full.</summary>
    public FactMaterial Material => FactCheckCorpus.MaterialOf(Basis);
}

/// <summary>Reading the fact-check corpus and turning every basis into the texts that are checked.</summary>
public static class FactCheckCorpus
{
    /// <summary>Bases of the corpus (contract stage 3, 3.2).</summary>
    public const int ExpectedBases = 16;

    /// <summary>Kind of a basis whose material has a mistake of its own.</summary>
    public const string FaultySourceKind = "faulty_source";

    /// <summary>Case name of the fixed parents' evening basis.</summary>
    public const string ParentsEvening = "elternabend";

    /// <summary>Plant kind of the invented way to reply or get in touch, which the parents' evening must find.</summary>
    public const string InventedContact = "invented_contact";

    /// <summary>Question c (problems of the material); reported, not part of the hit rate of the gate.</summary>
    public const string QuestionC = "c";

    /// <summary>Plant kinds and the question of the fact check they belong to.</summary>
    public static IReadOnlyDictionary<string, string> PlantQuestions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["changed_number"] = "a",
        ["changed_date"] = "a",
        ["changed_name"] = "a",
        ["invented_fact"] = "a",
        [InventedContact] = "a",
        ["assumed_action"] = "a",
        ["broken_instruction"] = "b",
        ["superseded_use"] = "superseded",
        ["silent_total"] = QuestionC,
    };

    private static readonly JsonSerializerOptions Json = new();

    /// <summary>All bases of a JSONL file in their order; a missing file has none.</summary>
    public static IReadOnlyList<CorpusBasis> Load(string path) =>
        File.Exists(path)
            ? File.ReadLines(path).Where(l => l.TrimStart().StartsWith('{')).Select(l => JsonSerializer.Deserialize<CorpusBasis>(l, Json)!).ToList()
            : [];

    /// <summary>
    /// Ids of the held-out bases, fixed before the checker ran for the first time: short and long, a revision, a profile and a
    /// basis with a faulty file. The hash of the request had left a single held-out basis, too few for the gate.
    /// </summary>
    public static readonly IReadOnlyList<string> HeldOutIds = ["fc-02", "fc-04", "fc-06", "fc-10", "fc-14"];

    /// <summary>Split of a basis: held out when its id is one of <see cref="HeldOutIds"/>; the parents' evening is always in the development set.</summary>
    public static string SplitOf(CorpusBasis basis) =>
        basis.Case != ParentsEvening && HeldOutIds.Contains(basis.Id) ? EvalBasics.Holdout : EvalBasics.Dev;

    /// <summary>The texts of a basis: the clean text (faulty when the labeller marked it) and, once planted, the planted copy.</summary>
    public static IReadOnlyList<FactCheckItem> Items(CorpusBasis basis)
    {
        var labels = (basis.Labels ?? []).Select(l => new ExpectedError("labelled", "a", l.Quote)).ToList();
        var items = new List<FactCheckItem> { new(basis, "base", basis.CleanText, labels) };
        if (basis.PlantedText is { Length: > 0 } planted)
        {
            var errors = (basis.Plants ?? [])
                .Select(p => new ExpectedError(p.Kind, PlantQuestions.GetValueOrDefault(p.Kind, "a"), p.Inserted))
                .Concat(labels.Where(l => planted.Contains(l.Wording, StringComparison.Ordinal)))
                .ToList();
            items.Add(new FactCheckItem(basis, "planted", planted, errors));
        }
        return items;
    }

    /// <summary>All texts of all bases.</summary>
    public static IReadOnlyList<FactCheckItem> Items(IEnumerable<CorpusBasis> bases) => bases.SelectMany(Items).ToList();

    /// <summary>The material of a basis, built like the direct path: no assumptions, no notes of the writer, full files.</summary>
    public static FactMaterial MaterialOf(CorpusBasis basis) => new(
        basis.Request,
        basis.Answers ?? [],
        [],
        ProfilePrompt.Render(ProfileEntity(basis.Profile)),
        (basis.Files ?? []).Select(f => new FactSource(f.Id, f.Name, f.Text)).ToList(),
        FactCheckScope.Full,
        null);

    /// <summary>Why the corpus cannot be measured; empty when it can.</summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<CorpusBasis> bases)
    {
        var problems = new List<string>();
        if (bases.Count != ExpectedBases)
            problems.Add($"the corpus has {bases.Count} of {ExpectedBases} bases");
        var duplicate = bases.GroupBy(b => b.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicate.Count > 0)
            problems.Add($"duplicate basis ids ({EvalBasics.Few(duplicate)})");
        var unlabelled = bases.Where(b => b.Labels is null).Select(b => b.Id).ToList();
        if (unlabelled.Count > 0)
            problems.Add($"{unlabelled.Count} bases are not labelled ({EvalBasics.Few(unlabelled)})");
        var unplanted = bases.Where(b => b.PlantedText is not { Length: > 0 } || b.Plants is not { Count: > 0 }).Select(b => b.Id).ToList();
        if (unplanted.Count > 0)
            problems.Add($"{unplanted.Count} bases have no planted copy ({EvalBasics.Few(unplanted)})");
        var notVerbatim = bases.Where(b => !Verbatim(b)).Select(b => b.Id).ToList();
        if (notVerbatim.Count > 0)
            problems.Add($"{notVerbatim.Count} bases have a label, plant or source quote that is not word for word in its text ({EvalBasics.Few(notVerbatim)})");
        var evening = bases.Where(b => b.Case == ParentsEvening).ToList();
        if (evening.Count != 1)
            problems.Add($"the corpus has {evening.Count} parents' evening bases instead of 1");
        else if (evening[0].Plants?.Any(p => p.Kind == InventedContact) != true)
            problems.Add("the parents' evening has no planted invented way to reply");
        else if (evening[0].SourceErrors?.Any(e => e.Kind == "wrong_total" && e.Stated is not null) != true)
            problems.Add("the parents' evening has no wrong total in its files");
        if (!bases.Any(b => SplitOf(b) == EvalBasics.Holdout))
            problems.Add("the held-out set is empty");
        return problems;
    }

    private static bool Verbatim(CorpusBasis basis)
    {
        var files = basis.Files ?? [];
        bool InFiles(string quote) => files.Any(f => f.Text.Contains(quote, StringComparison.Ordinal));
        return (basis.Labels ?? []).All(l => basis.CleanText.Contains(l.Quote, StringComparison.Ordinal))
            && (basis.Plants ?? []).All(p => basis.PlantedText?.Contains(p.Inserted, StringComparison.Ordinal) == true)
            && (basis.SourceErrors ?? []).All(e => InFiles(e.Quote) && (e.Quote2 is null || InFiles(e.Quote2)));
    }

    private static UserProfileEntity? ProfileEntity(CorpusProfile? profile) => profile is null ? null : new UserProfileEntity
    {
        Username = "factcheck-eval",
        DisplayName = profile.DisplayName,
        Role = profile.Role,
        Organization = profile.Organization,
        Signature = profile.Signature,
        NoGos = profile.NoGos,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
