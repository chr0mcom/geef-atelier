namespace Geef.Atelier.Mcp.Models;

/// <summary>Answer of <c>prepare_attachment</c>.</summary>
/// <param name="UploadId">Id of the upload; pass it in <c>uploadIds</c> of <c>submit_order</c> once the file was uploaded. Not secret.</param>
/// <param name="CurlCommand">Command that uploads the file with its one-time token in a header; replace the path placeholder with the path of the file.</param>
/// <param name="UploadPage">Link to the upload page for the person; it holds no secret and works only after the person signed in.</param>
/// <param name="TokenExpiresAt">When the token of <paramref name="CurlCommand"/> stops working.</param>
/// <param name="PageExpiresAt">When <paramref name="UploadPage"/> stops working.</param>
/// <param name="MaxBytes">Largest file that is accepted, in bytes.</param>
/// <param name="Next">What to do next, in plain English.</param>
public sealed record PreparedAttachmentDto(
    string UploadId,
    string CurlCommand,
    string UploadPage,
    DateTimeOffset TokenExpiresAt,
    DateTimeOffset PageExpiresAt,
    long MaxBytes,
    string Next);
