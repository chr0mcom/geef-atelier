namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>A selection of pages such as <c>1-40,55</c>: inclusive, 1-based ranges.</summary>
public sealed record PageSelection(IReadOnlyList<(int From, int To)> Ranges)
{
    /// <summary>
    /// Parses <paramref name="text"/> against a document with <paramref name="pageCount"/> pages. Returns false with a
    /// plain-English <paramref name="error"/> for malformed or out-of-range input.
    /// </summary>
    public static bool TryParse(string? text, int pageCount, out PageSelection? selection, out string? error) =>
        throw new NotImplementedException();

    /// <summary>True when <paramref name="page"/> lies in one of the ranges.</summary>
    public bool Contains(int page) =>
        throw new NotImplementedException();

    /// <summary>Keeps only the selected pages of extracted text, including their page markers.</summary>
    public string ApplyTo(string extractedText) =>
        throw new NotImplementedException();

    /// <summary>Returns the canonical form, e.g. <c>1-40,55</c>.</summary>
    public override string ToString() =>
        throw new NotImplementedException();
}
