namespace Geef.Atelier.Core.Domain.Research;

/// <summary>Topics that may go to the search, and how many were withheld or over the limit.</summary>
public sealed record QuerySelection(IReadOnlyList<string> Sent, int Withheld, int OverLimit);

/// <summary>
/// Decides without an AI model which research topics may leave the system. A topic goes out only when it holds no word of the files
/// or the About me details that is missing from the person's own request, and nothing unsafe (links, addresses, account or access details).
/// </summary>
public static class ResearchQueryGate
{
    /// <summary>Selects at most <paramref name="max"/> topics in the order given.</summary>
    public static QuerySelection Select(IReadOnlyList<string> topics, string requestText, IReadOnlyList<string> fileTexts, string? profileText, int max) =>
        throw new NotImplementedException();
}
