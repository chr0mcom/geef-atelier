using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>
/// Reads the <c>attachments</c> argument of the MCP tools (JSON array of <c>{filename, contentType, contentBase64}</c>)
/// and rejects what is obviously not the file itself: invalid Base64, empty or too short content, placeholder text such as
/// "see attached full version", Base64 encoded twice, and content whose magic bytes do not match the declared type.
/// The real reading of the file happens later in the source intake; this check only catches what a client typically
/// sends when it could not pass the file. Every rejection is an <see cref="McpAttachmentRejectedException"/> whose message names
/// the right way and which carries the code that is counted.
/// </summary>
internal static partial class McpAttachmentParser
{
    public const string PdfContentType = "application/pdf";
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string MarkdownContentType = "text/markdown";
    public const string PlainTextContentType = "text/plain";

    public const int MinTextCharacters = 10;
    public const int PlaceholderCheckMaxCharacters = 300;
    public const int Base64RunMinCharacters = 64;
    public const int PdfHeaderSearchBytes = 1024;

    private const string RightWayStart =
        "Attach the file with its original content as Base64 in contentBase64 and a matching contentType " +
        "(application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document, text/markdown or text/plain). " +
        "Never send a placeholder, a summary or shortened content instead of the file. ";

    public const string RightWay =
        RightWayStart +
        "If the file is too large to pass as an argument, ask the person to start the order in the Atelier web app and add the file there.";

    public const string RightWayWithTickets =
        RightWayStart +
        "If the file is larger than a few kilobytes, call prepare_attachment instead and upload the file with its command or link.";

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly IReadOnlyDictionary<string, string> ContentTypeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [PdfContentType] = PdfContentType,
            [DocxContentType] = DocxContentType,
            [MarkdownContentType] = MarkdownContentType,
            ["text/x-markdown"] = MarkdownContentType,
            [PlainTextContentType] = PlainTextContentType,
        };

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>The sentences about the right way: <see cref="RightWayWithTickets"/> while upload tickets are on, otherwise <see cref="RightWay"/>.</summary>
    public static string RightWayFor(bool tickets) => tickets ? RightWayWithTickets : RightWay;

    /// <summary>
    /// Parses and checks the attachments; returns an empty list for <c>null</c>, blank or <c>[]</c>. With <paramref name="tickets"/>
    /// the messages end with <see cref="RightWayWithTickets"/>, otherwise with <see cref="RightWay"/>.
    /// </summary>
    public static IReadOnlyList<(string FileName, string ContentType, byte[] Content)> Parse(string? attachmentsJson, bool tickets = false)
    {
        var rightWay = RightWayFor(tickets);
        if (string.IsNullOrWhiteSpace(attachmentsJson))
            return [];

        AttachmentDto?[]? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AttachmentDto?[]>(attachmentsJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new McpAttachmentRejectedException(
                SourceRefusalCodes.McpJson, null, null, null,
                "attachments must be a JSON array of objects with filename, contentType and contentBase64. " + rightWay, ex);
        }

        if (parsed is null || parsed.Length == 0)
            return [];

        var result = new List<(string, string, byte[])>(parsed.Length);
        foreach (var dto in parsed)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Filename))
                throw new McpAttachmentRejectedException(
                    SourceRefusalCodes.McpJson, null, NormalizeDeclaredType(dto?.ContentType), null, "Every attachment needs a filename. " + rightWay);
            result.Add(Check(dto, rightWay));
        }

        return result;
    }

    private static (string FileName, string ContentType, byte[] Content) Check(AttachmentDto dto, string rightWay)
    {
        var fileName = dto.Filename.Trim();
        var contentType = NormalizeContentType(dto.ContentType)
            ?? throw new McpAttachmentRejectedException(
                SourceRefusalCodes.McpUnsupportedType, fileName, NormalizeDeclaredType(dto.ContentType), null,
                $"Content type '{dto.ContentType}' of attachment '{fileName}' is not supported. " + rightWay);

        var refusal = new Refusal(fileName, contentType, null, rightWay);
        var content = DecodeBase64(refusal, dto.ContentBase64);
        refusal = refusal with { SizeBytes = content.LongLength };
        if (content.Length == 0)
            throw refusal.Rejected(SourceRefusalCodes.McpEmpty, "is empty");

        switch (contentType)
        {
            case PdfContentType when !ContainsPdfHeader(content):
                throw refusal.Rejected(MismatchCode(content), "is declared as PDF, but its content is not a PDF file" + DescribeActualContent(content));
            case DocxContentType when !content.AsSpan().StartsWith(ZipMagic):
                throw refusal.Rejected(MismatchCode(content), "is declared as a Word document, but its content is not a .docx file" + DescribeActualContent(content));
            case MarkdownContentType or PlainTextContentType:
                CheckText(refusal, content);
                break;
        }

        return (fileName, contentType, content);
    }

    private static void CheckText(Refusal refusal, byte[] content)
    {
        if (content.AsSpan().StartsWith(PdfMagic))
            throw refusal.Rejected(SourceRefusalCodes.McpTypeMismatch, "is a PDF file, but it was declared as text; use contentType application/pdf");
        if (content.AsSpan().StartsWith(ZipMagic))
            throw refusal.Rejected(SourceRefusalCodes.McpTypeMismatch, "is a binary file (for example a Word document), but it was declared as text; use the matching contentType");

        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(content).Trim('﻿').Trim();
        var visible = text.Count(c => !char.IsWhiteSpace(c));
        if (visible == 0)
            throw refusal.Rejected(SourceRefusalCodes.McpEmpty, "contains no text");
        if (visible < MinTextCharacters)
            throw refusal.Rejected(SourceRefusalCodes.McpPlaceholder, $"contains only {visible} characters, which looks like a placeholder rather than a file");

        if (IsBase64Run(text))
            throw refusal.Rejected(SourceRefusalCodes.McpBase64Invalid, "contains Base64 text instead of readable text (the file was probably encoded twice)");

        if (text.Length <= PlaceholderCheckMaxCharacters && PlaceholderPattern().IsMatch(text))
            throw refusal.Rejected(SourceRefusalCodes.McpPlaceholder, "contains only a placeholder or a reference to the file instead of the file content");
    }

    private static string MismatchCode(byte[] content)
    {
        if (content.AsSpan().StartsWith(ZipMagic) || content.AsSpan().StartsWith(PdfMagic))
            return SourceRefusalCodes.McpTypeMismatch;
        var sample = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 512));
        if (IsBase64Run(sample.Trim()))
            return SourceRefusalCodes.McpBase64Invalid;
        return sample.All(c => !char.IsControl(c) || char.IsWhiteSpace(c))
            ? SourceRefusalCodes.McpPlaceholder
            : SourceRefusalCodes.McpTypeMismatch;
    }

    /// <summary>
    /// The declared type as it is counted: the media type in lower case when it looks like <c>type/subtype</c> and has at most
    /// <see cref="SourceRefusalCodes.MaxDeclaredTypeLength"/> characters; otherwise <c>null</c>, so free text never reaches the count.
    /// </summary>
    internal static string? NormalizeDeclaredType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;
        var mediaType = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return mediaType.Length <= SourceRefusalCodes.MaxDeclaredTypeLength && MediaTypePattern().IsMatch(mediaType) ? mediaType : null;
    }

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return ContentTypeAliases.TryGetValue(mediaType, out var normalized) ? normalized : null;
    }

    private static byte[] DecodeBase64(Refusal refusal, string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw refusal.Rejected(SourceRefusalCodes.McpEmpty, "has no content");

        var compact = WhitespacePattern().Replace(base64, string.Empty);
        var buffer = new byte[compact.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64String(compact, buffer, out var written))
            throw refusal.Rejected(SourceRefusalCodes.McpBase64Invalid, "has content that is not valid Base64 (it may be shortened or contain a placeholder)");
        return buffer.AsSpan(0, written).ToArray();
    }

    private static bool ContainsPdfHeader(byte[] content) =>
        content.AsSpan(0, Math.Min(content.Length, PdfHeaderSearchBytes)).IndexOf(PdfMagic) >= 0;

    private static string DescribeActualContent(byte[] content)
    {
        if (content.AsSpan().StartsWith(ZipMagic))
            return " (it looks like a ZIP or Word file)";
        var sample = Encoding.UTF8.GetString(content, 0, Math.Min(content.Length, 512));
        if (IsBase64Run(sample.Trim()))
            return " (it looks like Base64 text, so the file was probably encoded twice)";
        return sample.All(c => !char.IsControl(c) || char.IsWhiteSpace(c))
            ? " (it looks like plain text, for example a placeholder)"
            : string.Empty;
    }

    private static bool IsBase64Run(string text)
    {
        var compact = WhitespacePattern().Replace(text, string.Empty);
        if (compact.Length < Base64RunMinCharacters || compact.Length % 4 != 0 || !Base64AlphabetPattern().IsMatch(compact))
            return false;
        return Convert.TryFromBase64String(compact, new byte[compact.Length], out _);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"^[A-Za-z0-9+/]+={0,2}$")]
    private static partial Regex Base64AlphabetPattern();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9!#$&^_.+-]{0,62}/[a-z0-9][a-z0-9!#$&^_.+-]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex MediaTypePattern();

    [GeneratedRegex(
        @"siehe\s+(die\s+|den\s+)?(angeh[äa]ngte|anhang|anlage|beigef[üu]gte)" +
        @"|\b(see|refer\s+to)\s+(the\s+)?(attached|attachment|enclosed)" +
        @"|\b(attached|enclosed)\s+(full\s+)?(file|document|version|pdf|text)" +
        @"|vollversion|vollst[äa]ndige\s+(fassung|version|datei)|\bfull\s+(version|text|document)\b" +
        @"|\b(content|text|file|inhalt|datei)\s+(omitted|truncated|removed|not\s+included|ausgelassen|gek[üu]rzt|entfernt)" +
        @"|\b(placeholder|platzhalter)\b" +
        @"|^\s*\[[^\]\n]{0,60}\]\s*$" +
        @"|^\s*(\.\.\.|…)+\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    private sealed record Refusal(string FileName, string ContentType, long? SizeBytes, string Way)
    {
        public McpAttachmentRejectedException Rejected(string code, string problem) =>
            new(code, FileName, ContentType, SizeBytes, $"The attachment '{FileName}' {problem}. {Way}");
    }

    private sealed record AttachmentDto(
        [property: JsonRequired] string Filename,
        [property: JsonRequired] string ContentType,
        [property: JsonRequired] string ContentBase64);
}
