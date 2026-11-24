namespace Geef.Atelier.Tests.TestData;

/// <summary>Builds synthetic PDF and Word documents in memory for source extraction tests.</summary>
internal static class SyntheticDocuments
{
    /// <summary>Builds a PDF with <paramref name="pages"/> pages; <paramref name="pageText"/> returns the text of each 1-based page.</summary>
    public static byte[] Pdf(int pages, Func<int, string> pageText) =>
        throw new NotImplementedException();

    /// <summary>Builds a Word document with one paragraph per entry of <paramref name="paragraphs"/>.</summary>
    public static byte[] Docx(IReadOnlyList<string> paragraphs) =>
        throw new NotImplementedException();
}
