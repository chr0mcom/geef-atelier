using System.Text;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>
/// Reads text and Markdown files: a byte order mark decides the encoding; without one, valid UTF-8 is read as UTF-8,
/// text with many zero bytes as UTF-16, and anything else as Windows-1252 with a warning. The characters are counted
/// before the text is decoded; a file with more than <see cref="SourceOptions.MaxExtractedChars"/> is refused without
/// building its text.
/// </summary>
internal sealed class PlainTextSourceExtractor(IOptions<SourceOptions> options) : ISourceTextExtractor
{
    internal const string EmptyReason = "This file has no text.";

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static PlainTextSourceExtractor() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public SourceKind Kind => SourceKind.Text;

    public Task<SourceExtraction> ExtractAsync(ReadOnlyMemory<byte> content, CancellationToken ct) =>
        Task.FromResult(Extract(content.Span, options.Value.MaxExtractedChars));

    internal static SourceExtraction Extract(ReadOnlySpan<byte> bytes, int maxChars = int.MaxValue)
    {
        var warnings = new List<SourceWarning>();
        var (encoding, start) = ChooseEncoding(bytes, warnings);
        var body = bytes[start..];
        if (encoding.GetCharCount(body) > maxChars)
            return SourceExtraction.TooMuchText();

        var text = encoding.GetString(body)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

        if (text.Length == 0)
            return SourceExtraction.Unreadable(EmptyReason);

        var replacements = text.Count(c => c == '\uFFFD');
        if (replacements > 0)
            warnings.Add(new SourceWarning(SourceWarningCodes.ReplacementCharacters,
                "Some characters could not be read and were replaced."));

        var quality = replacements > text.Length / 20 ? SourceQuality.Poor : SourceQuality.Good;
        return new SourceExtraction(text, null, [], quality, warnings, null);
    }

    private static (Encoding Encoding, int Start) ChooseEncoding(ReadOnlySpan<byte> bytes, List<SourceWarning> warnings)
    {
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
            return (Encoding.UTF8, 3);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE, 0x00, 0x00]))
            return (Encoding.UTF32, 4);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            return (Encoding.Unicode, 2);
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            return (Encoding.BigEndianUnicode, 2);

        if (SourceKindDetector.LooksLikeUtf16(bytes, out var littleEndian))
        {
            warnings.Add(new SourceWarning(SourceWarningCodes.EncodingGuessed,
                "The text encoding was guessed (UTF-16). If characters look wrong, save the file as UTF-8."));
            return (littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode, 0);
        }

        try
        {
            StrictUtf8.GetCharCount(bytes);
            return (StrictUtf8, 0);
        }
        catch (DecoderFallbackException)
        {
            warnings.Add(new SourceWarning(SourceWarningCodes.EncodingGuessed,
                "The text encoding was guessed (Windows-1252). If characters look wrong, save the file as UTF-8."));
            return (Encoding.GetEncoding(1252), 0);
        }
    }
}
