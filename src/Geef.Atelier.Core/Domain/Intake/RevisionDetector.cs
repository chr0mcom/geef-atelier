namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>A file of the order that could be the text to revise; <see cref="FileNumber"/> is its number in the clarifier prompt.</summary>
public sealed record RevisionCandidate(int FileNumber, Guid SourceId, string FileName);

/// <summary>Recognises a request to revise an attached text. Conservative: without a revision verb no file becomes the template.</summary>
public static class RevisionDetector
{
    /// <summary>Verb stems, matched without regard to case; "…" separates the parts of a split verb.</summary>
    public static IReadOnlyList<string> Verbs { get; } =
    [
        "überarbeit", "verbesser", "kürz", "korrigier", "redigier", "glätte", "schärf", "umschreib", "formulier … um",
        "lektorier", "prüf … und verbesser",
        "revise", "improve", "shorten", "rewrite", "edit", "proofread", "polish", "tighten", "rephrase",
    ];

    /// <summary>True when the request contains a revision verb.</summary>
    public static bool MentionsRevision(string requestText) => throw new NotImplementedException();

    /// <summary>The file to revise: requires a revision verb in the request; with one file that file, with several the model's choice when valid, otherwise none.</summary>
    public static RevisionCandidate? Choose(string requestText, IReadOnlyList<RevisionCandidate> files, int? modelChoice, bool modelSaysRevision) =>
        throw new NotImplementedException();
}
