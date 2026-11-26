namespace Geef.Atelier.Application.Sources;

/// <summary>Paths and the header of the upload ticket, the same for the web endpoints and the MCP tool. <c>{0}</c> is the upload id.</summary>
public static class UploadTicketRoutes
{
    /// <summary>Request header that carries the token; the token never goes into a path or a query.</summary>
    public const string TokenHeader = "X-Upload-Token";

    /// <summary>Path of the token upload (<c>PUT</c>).</summary>
    public const string TokenUploadPathFormat = "/api/uploads/{0}";

    /// <summary>Path of the upload from the page (<c>POST</c>, cookie and antiforgery header).</summary>
    public const string PageUploadPathFormat = "/api/uploads/{0}/page";

    /// <summary>Path of the upload page; it holds no secret and needs the owner's sign-in.</summary>
    public const string PagePathFormat = "/u/{0}";
}
