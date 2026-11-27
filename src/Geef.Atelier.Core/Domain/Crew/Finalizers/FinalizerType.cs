namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>Discriminator for the four finalizer executor strategies.</summary>
public enum FinalizerType
{
    /// <summary>Converts the final text to one or more file formats and stores them as run artifacts.</summary>
    FileExport = 0,

    /// <summary>Enriches the final text or its metadata (e.g. word count, reading level, front matter).</summary>
    MetadataEnrich = 1,

    /// <summary>Retired in stage 8c (D-089): sent the final text to a webhook or an e-mail address. Kept so stored rows and snapshots stay readable; no executor runs it.</summary>
    [Obsolete("Retired in stage 8c (D-089); no executor is registered for this type any more.")]
    ExternalSink = 2,

    /// <summary>Applies an LLM pass to transform the final text (e.g. anti-AI-voice polish, tone adjustment).</summary>
    Transform = 3,

    /// <summary>Extracts structured learnings from a completed run and publishes them to the continuous-learning loop.</summary>
    LearningExtract = 4,

    /// <summary>Evaluates a proposed learning entry through an LLM gate and approves or rejects it.</summary>
    LearningPublish = 5,

    /// <summary>Materializes a Crew-Spec JSON artifact (produced by a composition run) into real database entities and optionally chains a follow-up task run.</summary>
    CrewMaterialize = 6,
}

/// <summary>Helpers for <see cref="FinalizerType"/>.</summary>
public static class FinalizerTypeExtensions
{
#pragma warning disable CS0618
    private const FinalizerType RetiredExternalSink = FinalizerType.ExternalSink;
#pragma warning restore CS0618

    /// <summary>
    /// True for types whose executor was removed in stage 8c (D-089). Stored profiles and run snapshots may still name them:
    /// they stay readable, listings leave them out, a run skips them with a warning, and they can no longer be saved.
    /// </summary>
    public static bool IsRetired(this FinalizerType type) => type == RetiredExternalSink;

    /// <summary>The types a profile can still be saved with, in declaration order.</summary>
    public static IReadOnlyList<FinalizerType> Usable { get; } = [.. Enum.GetValues<FinalizerType>().Where(t => !t.IsRetired())];
}
