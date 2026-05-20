namespace Geef.Atelier.Infrastructure.Security;

public sealed record HtmlExtractionResult(string? Title, string Text);

public interface IHtmlContentExtractor
{
    Task<HtmlExtractionResult> ExtractAsync(string html, bool stripBoilerplate, CancellationToken ct = default);
}
