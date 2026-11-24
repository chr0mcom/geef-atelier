using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>The rendered source block of an order: the sources, how they were included and the background document handed to the writer.</summary>
internal sealed record OrderSourceBundle(IReadOnlyList<PromptSource> Sources, SourceUsageMode Mode, string ContextDocument, int TotalChars)
{
    /// <summary>A bundle without sources.</summary>
    public static OrderSourceBundle Empty { get; } = new([], SourceUsageMode.FullText, string.Empty, 0);
}
