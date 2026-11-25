using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>
/// A source as it goes into the writer's prompt: <see cref="Id"/> is <c>S1…Sn</c> by position, <see cref="Text"/> is
/// the extracted text after the page selection. <see cref="LookedUp"/> marks the passages looked up on the web (<c>W1</c>), which have
/// no source of the person.
/// </summary>
internal sealed record PromptSource(Guid SourceId, string Id, string FileName, SourceKind Kind, int? PageCount, string? PagesIncluded, string Text, int Characters,
    bool LookedUp = false);
