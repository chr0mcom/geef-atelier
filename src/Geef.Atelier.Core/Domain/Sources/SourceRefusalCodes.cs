namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>
/// Fixed codes of counted refusals (<see cref="SourceRefusalRecord.Code"/>). Codes are never renamed, so counts stay comparable;
/// new codes are added. Extraction failures are mapped to a code in the infrastructure, where their sentences live.
/// </summary>
public static class SourceRefusalCodes
{
    public const string TooLarge = "too_large";
    public const string UserBusy = "user_busy";
    public const string Busy = "busy";
    public const string Interrupted = "interrupted";
    public const string SaveFailed = "save_failed";
    public const string Empty = "empty";
    public const string Quota = "quota";
    public const string UnsupportedType = "unsupported_type";
    public const string OldWord = "old_word";
    public const string ArchiveRefused = "docx_archive";
    public const string PdfNoText = "pdf_no_text";
    public const string PdfEncrypted = "pdf_encrypted";
    public const string Unreadable = "unreadable";
    public const string Unavailable = "unavailable";
    public const string TooComplex = "too_complex";
    public const string Timeout = "timeout";
    public const string TooMuchText = "too_much_text";
    public const string TooManyPages = "too_many_pages";
    public const string SizeMismatch = "size_mismatch";
    public const string TooLongForTicket = "too_long_for_ticket";
    public const string McpJson = "mcp_json";
    public const string McpPlaceholder = "mcp_placeholder";
    public const string McpBase64Invalid = "mcp_base64_invalid";
    public const string McpTypeMismatch = "mcp_type_mismatch";
    public const string McpUnsupportedType = "mcp_unsupported_type";
    public const string McpEmpty = "mcp_empty";
    public const string Other = "other";

    /// <summary>
    /// Whether a refusal with <paramref name="code"/> uses one of the attempts of an upload ticket: only when the file itself was
    /// checked and refused. Too many uploads at once, a busy service, a broken-off upload, a failed save, extraction that is not
    /// available and a full quota say nothing about the file, so they use no attempt; nor does a refusal without a code.
    /// </summary>
    public static bool CountsAsAttempt(string? code) =>
        code is not (null or UserBusy or Busy or Interrupted or SaveFailed or Unavailable or Quota);

    /// <summary>Longest stored extension.</summary>
    public const int MaxExtensionLength = 8;

    /// <summary>Longest stored declared type.</summary>
    public const int MaxDeclaredTypeLength = 100;

    /// <summary>
    /// The extension of <paramref name="fileName"/> in lower case without the dot, only when it consists of <c>[a-z0-9]</c>
    /// and has at most <see cref="MaxExtensionLength"/> characters; otherwise <c>null</c>. Nothing else of the name is kept.
    /// </summary>
    public static string? ExtensionOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        var name = fileName.Trim();
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1 || name.Length - dot - 1 > MaxExtensionLength)
            return null;
        var extension = name[(dot + 1)..].ToLowerInvariant();
        return extension.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9') ? extension : null;
    }

    /// <summary>
    /// The media type of <paramref name="contentType"/> in lower case without parameters, only when it has the form
    /// <c>type/subtype</c> with token characters and at most <see cref="MaxDeclaredTypeLength"/> characters; otherwise <c>null</c>.
    /// </summary>
    public static string? DeclaredTypeOf(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;
        var semicolon = contentType.IndexOf(';');
        var type = (semicolon >= 0 ? contentType[..semicolon] : contentType).Trim().ToLowerInvariant();
        if (type.Length is 0 or > MaxDeclaredTypeLength)
            return null;
        var slash = type.IndexOf('/');
        if (slash <= 0 || slash == type.Length - 1 || type.IndexOf('/', slash + 1) >= 0)
            return null;
        return type.All(c => c == '/' || IsTokenCharacter(c)) ? type : null;
    }

    private static bool IsTokenCharacter(char c) =>
        c is >= 'a' and <= 'z' or >= '0' and <= '9' or '!' or '#' or '$' or '&' or '^' or '_' or '.' or '+' or '-';
}
