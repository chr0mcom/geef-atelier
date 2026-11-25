namespace Geef.Atelier.Mcp.Models;

/// <summary>Answer of <c>export_order</c>: the text of one version as a file, the same file the web app downloads.</summary>
/// <param name="FileName">File name, with the version (for example <c>letter-v2.docx</c>).</param>
/// <param name="ContentType">Media type of the file.</param>
/// <param name="ContentBase64">The complete file, Base64 encoded.</param>
/// <param name="Text">For <c>md</c>: the Markdown of the file as text; <c>null</c> for <c>docx</c> and <c>pdf</c>.</param>
public sealed record OrderExportDto(string FileName, string ContentType, string ContentBase64, string? Text = null);
