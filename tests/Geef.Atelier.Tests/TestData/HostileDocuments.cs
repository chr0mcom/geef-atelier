using System.IO.Compression;
using System.Text;

namespace Geef.Atelier.Tests.TestData;

/// <summary>Builds small hostile files (a few KB) that trigger the extraction limits within seconds.</summary>
internal static class HostileDocuments
{
    private const string ContentTypes = """<?xml version="1.0" encoding="UTF-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>""";
    private const string Relationships = """<?xml version="1.0" encoding="UTF-8"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""";

    /// <summary>A Word file whose body holds <paramref name="depth"/> nested content controls around one paragraph.</summary>
    public static byte[] DeeplyNestedDocx(int depth, string text = "deep")
    {
        var body = new StringBuilder(depth * 50);
        for (var i = 0; i < depth; i++)
            body.Append("<w:sdt><w:sdtContent>");
        body.Append("<w:p><w:r><w:t>").Append(text).Append("</w:t></w:r></w:p>");
        for (var i = 0; i < depth; i++)
            body.Append("</w:sdtContent></w:sdt>");
        return Docx(body.ToString());
    }

    /// <summary>
    /// A Word file of a few KB whose single text run holds <paramref name="chars"/> times the same letter; it compresses
    /// about 1000:1, so it passes only with the compression-ratio check switched off.
    /// </summary>
    public static byte[] TextBombDocx(int chars) =>
        Docx($"<w:p><w:r><w:t>{new string('a', chars)}</w:t></w:r></w:p>");

    /// <summary>
    /// A Word file with <paramref name="chars"/> characters of varied words in paragraphs of about 500 characters; it
    /// compresses only a few times, so it passes the archive checks with the default limits.
    /// </summary>
    public static byte[] WordyDocx(int chars, int seed = 1)
    {
        string[] vocabulary = ["offer", "garden", "planting", "lawn", "care", "season", "price", "client", "request", "winter", "summer", "hedge", "water", "stone", "path", "light", "soil", "seed", "flower", "tree"];
        var random = new Random(seed);
        var body = new StringBuilder(chars * 2);
        var written = 0;
        while (written < chars)
        {
            body.Append("<w:p><w:r><w:t>");
            var paragraph = 0;
            while (paragraph < 500 && written < chars)
            {
                var word = $"{vocabulary[random.Next(vocabulary.Length)]}{random.Next(1_000)} ";
                body.Append(word);
                paragraph += word.Length;
                written += word.Length;
            }

            body.Append("</w:t></w:r></w:p>");
        }

        return Docx(body.ToString());
    }

    /// <summary>A Word file with one <c>word/document.xml</c> whose body is <paramref name="bodyXml"/>.</summary>
    public static byte[] Docx(string bodyXml)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", ContentTypes);
            Write(zip, "_rels/.rels", Relationships);
            Write(zip, "word/document.xml",
                $"""<?xml version="1.0" encoding="UTF-8"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{bodyXml}</w:body></w:document>""");
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A one-page PDF whose content stream starts with <paramref name="leadingText"/> and then repeats <c>q Q</c> until it
    /// unpacks to <paramref name="inflatedBytes"/> bytes; the file stays tiny because the repetition compresses about 1000:1.
    /// </summary>
    public static byte[] FlateBombPdf(long inflatedBytes, string leadingText = "bomb")
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(Encoding.ASCII.GetBytes($"BT /F1 12 Tf 40 760 Td ({leadingText}) Tj ET\n"));
            var block = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("q Q ", 16_384)));
            for (long written = 0; written < inflatedBytes; written += block.Length)
                zlib.Write(block);
        }

        return Pdf(compressed.ToArray(), "/Filter /FlateDecode");
    }

    /// <summary>Writes a minimal one-page PDF around <paramref name="content"/> with a correct cross-reference table.</summary>
    public static byte[] Pdf(byte[] content, string filter = "")
    {
        var objects = new List<byte[]>
        {
            Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
            Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Encoding.ASCII.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>"),
            Encoding.ASCII.GetBytes($"<< /Length {content.Length} {filter} >>\nstream\n").Concat(content).Concat(Encoding.ASCII.GetBytes("\nendstream")).ToArray(),
            Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
        };

        using var pdf = new MemoryStream();
        pdf.Write("%PDF-1.7\n"u8);
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Position);
            pdf.Write(Encoding.ASCII.GetBytes($"{i + 1} 0 obj\n"));
            pdf.Write(objects[i]);
            pdf.Write("\nendobj\n"u8);
        }

        var xref = pdf.Position;
        var table = new StringBuilder($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            table.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        table.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        pdf.Write(Encoding.ASCII.GetBytes(table.ToString()));
        return pdf.ToArray();
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
        using var writer = entry.Open();
        writer.Write(Encoding.UTF8.GetBytes(content));
    }
}
