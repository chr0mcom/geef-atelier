using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;

namespace Geef.Atelier.Tests.Sources;

/// <summary>The one-pass preparation of extracted text: same result and counts as the separate steps it replaces.</summary>
public sealed class SourceTextNormalizerTests
{
    public static TheoryData<string> Texts => new()
    {
        string.Empty,
        "One line",
        "Trailing newline\n",
        "\n\nLeading blank lines\n\n\nand gaps\n",
        "[[page 1]]\nFirst page text\n[[page 2]]\n[[page 3]]\nThird page\n",
        "[[page 1]]\n[[page 2]]",
        "Text with [[page 4]] inside a line\n  [[page 5]]  \nnot a marker either",
        "Windows\r\nline\r\nends\r\n",
        "Lone\rcarriage\rreturns",
        "Unicode\u00a0space\u2003and\ttabs\u2028separator",
        "  padded words  \n\t\n  more  ",
        string.Join(' ', Enumerable.Repeat("word", 700)) + "\nshort",
        new string('x', 2_500) + "\n" + new string('y', 999) + " " + new string('z', 400),
        "[[page 1]]\n" + new string('a', 995) + " [[page 3]]\nmore text",
        "Emoji 😀 at the cut " + string.Concat(Enumerable.Repeat("😀", 600)),
    };

    [Theory]
    [MemberData(nameof(Texts))]
    public void PdfTextGivesTheSameResultAsWrappingAndCountingSeparately(string text)
    {
        var expected = ReferenceWrap(text, 1_000, out var expectedWrapped);

        var normalized = text.NormalizeForStorage(defuseMarkerLines: false, 1_000);

        Assert.Equal(expected, normalized.Text);
        Assert.Equal(expectedWrapped, normalized.Wrapped);
        Assert.Equal(PageMarkers.StripMarkers(normalized.Text).Length, normalized.CharCount);
        Assert.Equal(WordCounter.Count(PageMarkers.StripMarkers(normalized.Text)), normalized.WordCount);
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void OtherFilesAreNeutralisedAfterTheLineEndsAreUnified(string text)
    {
        var unified = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var expected = ReferenceWrap(unified.DefusePageMarkerLines(), 1_000, out var expectedWrapped);

        var normalized = text.NormalizeForStorage(defuseMarkerLines: true, 1_000);

        Assert.Equal(expected, normalized.Text);
        Assert.Equal(expectedWrapped, normalized.Wrapped);
        Assert.Equal([1], PageMarkers.SplitPages(normalized.Text).Select(p => p.Page));
        Assert.Equal(PageMarkers.StripMarkers(normalized.Text).Length, normalized.CharCount);
        Assert.Equal(WordCounter.Count(PageMarkers.StripMarkers(normalized.Text)), normalized.WordCount);
    }

    [Fact]
    public void TextThatNeedsNoChangeIsNotCopied()
    {
        var text = "[[page 1]]\nAlready clean text\n[[page 2]]\nwith short lines";

        Assert.Same(text, text.NormalizeForStorage(defuseMarkerLines: false, 1_000).Text);
        var plain = "Plain notes\nwithout markers";
        Assert.Same(plain, plain.NormalizeForStorage(defuseMarkerLines: true, 1_000).Text);
        Assert.NotSame(text, text.NormalizeForStorage(defuseMarkerLines: true, 1_000).Text);
    }

    [Theory]
    [MemberData(nameof(Texts))]
    public void PreviewEqualsTheFullStripAndCut(string text)
    {
        var stored = text.NormalizeForStorage(defuseMarkerLines: false, 1_000).Text;
        foreach (var max in new[] { 5, 20, 800 })
            Assert.Equal(ReferencePreview(stored, max), stored.PreviewWithoutPageMarkers(max));
        Assert.Equal(ReferencePreview(text, 20), text.PreviewWithoutPageMarkers(20));
    }

    private static string ReferencePreview(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var plain = PageMarkers.StripMarkers(text).Trim();
        if (plain.Length <= maxChars)
            return plain;
        var cut = maxChars;
        if (char.IsHighSurrogate(plain[cut - 1]))
            cut--;
        return plain[..cut].TrimEnd() + "…";
    }

    private static string ReferenceWrap(string text, int maxLineChars, out bool wrapped)
    {
        wrapped = false;
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (normalized.Split('\n').All(l => l.Length <= maxLineChars))
            return normalized;

        var builder = new System.Text.StringBuilder();
        var first = true;
        foreach (var line in normalized.Split('\n'))
        {
            if (!first)
                builder.Append('\n');
            first = false;
            if (line.Length <= maxLineChars)
            {
                builder.Append(line);
                continue;
            }

            wrapped = true;
            var rest = line.AsSpan();
            while (rest.Length > maxLineChars)
            {
                var cut = rest[..(maxLineChars + 1)].LastIndexOf(' ');
                if (cut < maxLineChars / 2)
                {
                    cut = maxLineChars;
                    if (char.IsHighSurrogate(rest[cut - 1]))
                        cut--;
                    builder.Append(rest[..cut].ToString().DefusePageMarker()).Append('\n');
                    rest = rest[cut..];
                }
                else
                {
                    builder.Append(rest[..cut].TrimEnd().ToString().DefusePageMarker()).Append('\n');
                    rest = rest[(cut + 1)..];
                }
            }

            builder.Append(rest.ToString().DefusePageMarker());
        }

        return builder.ToString();
    }
}
