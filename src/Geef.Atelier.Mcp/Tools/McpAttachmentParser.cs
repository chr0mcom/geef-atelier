using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ModelContextProtocol;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>
/// Reads the <c>attachments</c> argument of the MCP tools (JSON array of <c>{filename, contentType, contentBase64}</c>)
/// and rejects what is obviously not the file itself: invalid Base64, empty or too short content, placeholder text such as
/// "see attached full version", Base64 encoded twice, and content whose magic bytes do not match the declared type.
/// The real reading of the file happens later in the source intake; this check only catches what a client typically
/// sends when it could not pass the file. Every rejection is an <see cref="McpException"/> whose message names the right way.
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

    public const string RightWay =
        "Attach the file with its original content as Base64 in contentBase64 and a matching contentType " +
        "(application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document, text/markdown or text/plain). " +
        "Never send a placeholder, a summary or shortened content instead of the file. " +
        "If the file is too large to pass as an argument, ask the person to start the order in the Atelier web app and add the file there.";

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

    /// <summary>Parses and checks the attachments; returns an empty list for <c>null</c>, blank or <c>[]</c>.</summary>
    public static IReadOnlyList<(string FileName, string ContentType, byte[] Content)> Parse(string? attachmentsJson)
    {
        if (string.IsNullOrWhiteSpace(attachmentsJson))
            return [];

        AttachmentDto?[]? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AttachmentDto?[]>(attachmentsJson, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new McpException(
                "attachments must be a JSON array of objects with filename, contentType and contentBase64. " + RightWay, ex);
        }

        if (parsed is null || parsed.Length == 0)
            return [];

        var result = new List<(string, string, byte[])>(parsed.Length);
        foreach (var dto in parsed)
        {
            if (dto is null || string.IsNullOrWhiteSpace(dto.Filename))
                throw new McpException("Every attachment needs a filename. " + RightWay);
            result.Add(Check(dto));
        }

        return result;
    }

    private static (string FileName, string ContentType, byte[] Content) Check(AttachmentDto dto)
    {
        var fileName = dto.Filename.Trim();
        var contentType = NormalizeContentType(dto.ContentType)
            ?? throw new McpException(
                $"Content type '{dto.ContentType}' of attachment '{fileName}' is not supported. " + RightWay);

        var content = DecodeBase64(fileName, dto.ContentBase64);
        if (content.Length == 0)
            throw Rejected(fileName, "is empty");

        switch (contentType)
        {
            case PdfContentType when !ContainsPdfHeader(content):
                throw Rejected(fileName, "is declared as PDF, but its content is not a PDF file" + DescribeActualContent(content));
            case DocxContentType when !content.AsSpan().StartsWith(ZipMagic):
                throw Rejected(fileName, "is declared as a Word document, but its content is not a .docx file" + DescribeActualContent(content));
            case MarkdownContentType or PlainTextContentType:
                CheckText(fileName, content);
                break;
        }

        return (fileName, contentType, content);
    }

    private static void CheckText(string fileName, byte[] content)
    {
        if (content.AsSpan().StartsWith(PdfMagic))
            throw Rejected(fileName, "is a PDF file, but it was declared as text; use contentType application/pdf");
        if (content.AsSpan().StartsWith(ZipMagic))
            throw Rejected(fileName, "is a binary file (for example a Word document), but it was declared as text; use the matching contentType");

        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false).GetString(content).Trim('﻿').Trim();
        var visible = text.Count(c => !char.IsWhiteSpace(c));
        if (visible == 0)
            throw Rejected(fileName, "contains no text");
        if (visible < MinTextCharacters)
            throw Rejected(fileName, $"contains only {visible} characters, which looks like a placeholder rather than a file");

        if (IsBase64Run(text))
            throw Rejected(fileName, "contains Base64 text instead of readable text (the file was probably encoded twice)");

        if (text.Length <= PlaceholderCheckMaxCharacters && PlaceholderPattern().IsMatch(text))
            throw Rejected(fileName, "contains only a placeholder or a reference to the file instead of the file content");
    }

    private static McpException Rejected(string fileName, string problem) =>
        new($"The attachment '{fileName}' {problem}. {RightWay}");

    private static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return ContentTypeAliases.TryGetValue(mediaType, out var normalized) ? normalized : null;
    }

    private static byte[] DecodeBase64(string fileName, string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            throw Rejected(fileName, "has no content");

        var compact = WhitespacePattern().Replace(base64, string.Empty);
        var buffer = new byte[compact.Length * 3 / 4 + 3];
        if (!Convert.TryFromBase64String(compact, buffer, out var written))
            throw Rejected(fileName, "has content that is not valid Base64 (it may be shortened or contain a placeholder)");
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

    private sealed record AttachmentDto(
        [property: JsonRequired] string Filename,
        [property: JsonRequired] string ContentType,
        [property: JsonRequired] string ContentBase64);
}
