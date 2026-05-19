namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>Discriminator for the four finalizer executor strategies.</summary>
public enum FinalizerType
{
    /// <summary>Converts the final text to one or more file formats and stores them as run artifacts.</summary>
    FileExport = 0,

    /// <summary>Enriches the final text or its metadata (e.g. word count, reading level, front matter).</summary>
    MetadataEnrich = 1,

    /// <summary>Sends the final text to an external system such as a webhook or email address.</summary>
    ExternalSink = 2,

    /// <summary>Applies an LLM pass to transform the final text (e.g. anti-AI-voice polish, tone adjustment).</summary>
    Transform = 3,
}
