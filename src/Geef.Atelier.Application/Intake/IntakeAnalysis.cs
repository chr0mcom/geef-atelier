using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Persistence.Sources;

namespace Geef.Atelier.Application.Intake;

/// <summary>What the pure part of the intake decided: the gate result, the file to revise and the clarifier output as it is stored.</summary>
public sealed record IntakeAnalysisResult(QuestionGateResult Gate, RevisionCandidate? Revision, ClarifierOutput Output);

/// <summary>
/// The pure part of the intake, shared by the order intake and the measuring tool so that both judge the same pipeline:
/// the clarifier input from request, files and profile, and after the call the revision choice and the question gate.
/// </summary>
public static class IntakeAnalysis
{
    /// <summary>The clarifier input: the request, an excerpt of every file, the rendered profile and the revision hint.</summary>
    public static ClarifierRequest Request(string requestText, IReadOnlyList<OrderSourceItem> items, UserProfileEntity? profile, IntakeOptions options) =>
        new(requestText, IntakeExcerpts.Build(items, options), profile is null ? null : ProfilePrompt.Render(profile), RevisionDetector.MentionsRevision(requestText));

    /// <summary>The whole extracted texts of the files of an order, as the question gate compares answer options with them.</summary>
    public static IReadOnlyList<string> MaterialTexts(this IReadOnlyList<OrderSourceItem> items) =>
        items.Select(i => i.Source.ExtractedText).OfType<string>().Where(t => t.Length > 0).ToList();

    /// <summary>The files of a clarifier input that could be the text to revise, with their numbers in the prompt.</summary>
    public static IReadOnlyList<RevisionCandidate> Candidates(this ClarifierRequest request) =>
        request.Files.Select(f => new RevisionCandidate(f.Number, f.SourceId, f.FileName)).ToList();

    /// <summary>
    /// Chooses the file to revise (<see cref="RevisionDetector.Choose"/>) and applies the <see cref="QuestionGate"/>; the returned
    /// output carries the summary as the gate allows it to be shown. <paramref name="materialTexts"/> are the whole extracted texts of
    /// the files (not the excerpts of the prompt); with <paramref name="dropGuessedDates"/> the gate drops answer options naming a date,
    /// month or weekday that neither the request nor these texts name.
    /// </summary>
    public static IntakeAnalysisResult Analyse(
        string requestText,
        IReadOnlyList<RevisionCandidate> candidates,
        ClarifierOutput output,
        IReadOnlyDictionary<GapTopic, MemoryAnswer> known,
        int maxQuestions,
        IReadOnlyList<string>? materialTexts = null,
        bool dropGuessedDates = false)
    {
        var revision = RevisionDetector.Choose(requestText, candidates, output.RevisionFileNumber, output.IsRevision);
        var gate = QuestionGate.Apply(new QuestionGateInput(output, requestText, revision is not null, known, maxQuestions, materialTexts, dropGuessedDates));
        return new IntakeAnalysisResult(gate, revision, output with { Understanding = gate.Understanding });
    }
}
