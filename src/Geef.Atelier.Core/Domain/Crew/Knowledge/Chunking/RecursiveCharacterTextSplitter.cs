namespace Geef.Atelier.Core.Domain.Crew.Knowledge.Chunking;

/// <summary>
/// Splits text into overlapping chunks using the same recursive-character algorithm as LangChain's
/// <c>RecursiveCharacterTextSplitter</c>. Separators are tried in order; if splitting by the current
/// separator yields a chunk that is still too large, that chunk is recursed with the next separator.
/// </summary>
public sealed class RecursiveCharacterTextSplitter
{
    private static readonly string[] DefaultSeparators = ["\n\n", "\n", ". ", " ", ""];

    private readonly int _maxTokens;
    private readonly int _overlapTokens;

    /// <param name="maxTokens">Maximum tokens allowed per chunk (default 1000).</param>
    /// <param name="overlapTokens">Tokens from the end of the previous chunk prepended to the next (default 100).</param>
    public RecursiveCharacterTextSplitter(int maxTokens = 1000, int overlapTokens = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens, nameof(maxTokens));
        ArgumentOutOfRangeException.ThrowIfNegative(overlapTokens, nameof(overlapTokens));
        if (overlapTokens >= maxTokens)
            throw new ArgumentException($"overlapTokens ({overlapTokens}) must be less than maxTokens ({maxTokens}).", nameof(overlapTokens));
        _maxTokens = maxTokens;
        _overlapTokens = overlapTokens;
    }

    /// <summary>Splits <paramref name="text"/> into a list of non-empty <see cref="TextChunk"/> records.</summary>
    public IReadOnlyList<TextChunk> Split(string text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<TextChunk>();

        var rawChunks = new List<string>();
        SplitRecursive(text, 0, rawChunks);

        var result = new List<TextChunk>(rawChunks.Count);
        string? previousOverlap = null;

        for (var i = 0; i < rawChunks.Count; i++)
        {
            var content = previousOverlap != null
                ? previousOverlap + rawChunks[i]
                : rawChunks[i];

            result.Add(new TextChunk(i, content, EstimateTokens(content)));

            // Compute overlap tail to prepend to the next chunk.
            previousOverlap = TailByTokens(rawChunks[i], _overlapTokens);
        }

        return result;
    }

    private void SplitRecursive(string text, int separatorIndex, List<string> output)
    {
        if (EstimateTokens(text) <= _maxTokens)
        {
            output.Add(text);
            return;
        }

        // No more separators — emit as-is even if oversized.
        if (separatorIndex >= DefaultSeparators.Length)
        {
            output.Add(text);
            return;
        }

        var separator = DefaultSeparators[separatorIndex];

        string[] parts = separator.Length == 0
            ? SplitIntoCharacterChunks(text)
            : text.Split(separator);

        if (parts.Length == 1)
        {
            // Separator not found; try the next one.
            SplitRecursive(text, separatorIndex + 1, output);
            return;
        }

        // Merge parts back together up to the token limit before recursing.
        var buffer = string.Empty;
        foreach (var part in parts)
        {
            var candidate = buffer.Length == 0 ? part : buffer + separator + part;

            if (EstimateTokens(candidate) <= _maxTokens)
            {
                buffer = candidate;
            }
            else
            {
                if (buffer.Length > 0)
                {
                    SplitRecursive(buffer, separatorIndex + 1, output);
                    buffer = string.Empty;
                }

                // The single part might itself be oversized; recurse with the next separator.
                if (EstimateTokens(part) > _maxTokens)
                    SplitRecursive(part, separatorIndex + 1, output);
                else if (part.Length > 0)
                    buffer = part;
            }
        }

        if (buffer.Length > 0)
            SplitRecursive(buffer, separatorIndex + 1, output);
    }

    /// <summary>
    /// Returns a suffix of <paramref name="text"/> whose estimated token count is at most
    /// <paramref name="tokenCount"/>. Used to build the overlap prepended to the next chunk.
    /// </summary>
    private static string TailByTokens(string text, int tokenCount)
    {
        var targetChars = tokenCount * 4;
        if (text.Length <= targetChars)
            return text;
        return text[^targetChars..];
    }

    /// <summary>
    /// Splits text into sub-strings of approximately <c>_maxTokens * 4</c> characters
    /// when the empty-string separator is reached.
    /// </summary>
    private string[] SplitIntoCharacterChunks(string text)
    {
        var chunkSize = _maxTokens * 4;
        var count = (text.Length + chunkSize - 1) / chunkSize;
        var result = new string[count];
        for (var i = 0; i < count; i++)
        {
            var start = i * chunkSize;
            var length = Math.Min(chunkSize, text.Length - start);
            result[i] = text.Substring(start, length);
        }
        return result;
    }

    private static int EstimateTokens(string text) => (text.Length + 3) / 4;
}
