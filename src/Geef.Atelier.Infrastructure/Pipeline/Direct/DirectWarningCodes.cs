namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>Warning codes of the direct path, stored with run warnings like those in <c>RunWarningCodes</c>.</summary>
internal static class DirectWarningCodes
{
    /// <summary>A file of the order carries a warning from reading it (for example pages without text).</summary>
    public const string SourceWarning = "grounding.source_warning";

    /// <summary>The files of the order exceed the full-text budget although the submission should have refused them.</summary>
    public const string SourcesOverBudget = "grounding.sources_over_budget";

    /// <summary>The writer's input tokens suggest that it searched the files instead of reading them in full.</summary>
    public const string PartialRead = "writer.partial_read";
}
