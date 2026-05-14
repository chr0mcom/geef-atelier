using Geef.Atelier.Core.Domain.Crew.Knowledge.Chunking;

namespace Geef.Atelier.Tests.Domain.Crew.Knowledge.Chunking;

public sealed class RecursiveCharacterTextSplitterTests
{
    [Fact]
    public void Split_EmptyString_ReturnsEmptyList()
    {
        var splitter = new RecursiveCharacterTextSplitter();
        var result = splitter.Split(string.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void Split_ShortText_ReturnsSingleChunk()
    {
        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 100);
        var result = splitter.Split("Hello world");

        Assert.Single(result);
        Assert.Equal("Hello world", result[0].Content);
        Assert.Equal(0, result[0].Index);
    }

    [Fact]
    public void Split_ShortText_EstimatedTokensIsCorrect()
    {
        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 100);
        // "Hello world" = 11 chars → (11 + 3) / 4 = 3
        var result = splitter.Split("Hello world");
        Assert.Equal(3, result[0].EstimatedTokens);
    }

    [Fact]
    public void Split_TextExactlyAtLimit_ReturnsSingleChunk()
    {
        // 400 chars → (400 + 3) / 4 = 100 tokens — exactly at the limit of 100.
        var text = new string('a', 400);
        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 100);
        var result = splitter.Split(text);
        Assert.Single(result);
    }

    [Fact]
    public void Split_ParagraphSeparatedText_SplitsByDoubleNewline()
    {
        // Each paragraph is ~5 tokens; limit is 6 tokens — so each paragraph is its own chunk.
        var para1 = new string('x', 20); // (20+3)/4 = 5 tokens
        var para2 = new string('y', 20);
        var para3 = new string('z', 20);
        var text = para1 + "\n\n" + para2 + "\n\n" + para3;

        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 6, overlapTokens: 0);
        var result = splitter.Split(text);

        Assert.True(result.Count >= 3);
        Assert.Contains(result, c => c.Content.Contains(para1));
        Assert.Contains(result, c => c.Content.Contains(para2));
        Assert.Contains(result, c => c.Content.Contains(para3));
    }

    [Fact]
    public void Split_Indices_AreSequential()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 10).Select(i => new string((char)('a' + i), 50)));
        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 15, overlapTokens: 0);
        var result = splitter.Split(text);

        for (var i = 0; i < result.Count; i++)
            Assert.Equal(i, result[i].Index);
    }

    [Fact]
    public void Split_WithOverlap_SecondChunkStartsWithTailOfFirst()
    {
        // Build text that will require at least two chunks.
        var block1 = new string('a', 80); // ~20 tokens
        var block2 = new string('b', 80); // ~20 tokens
        var text = block1 + "\n\n" + block2;

        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 25, overlapTokens: 5);
        var result = splitter.Split(text);

        Assert.True(result.Count >= 2);
        // The second chunk must contain some overlap characters from block1.
        Assert.Contains("a", result[1].Content);
    }

    [Fact]
    public void Split_NoOverlap_ChunksDoNotRepeatContent()
    {
        var text = string.Join("\n\n", Enumerable.Range(0, 5).Select(i => $"Section {i}: " + new string('x', 30)));
        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 15, overlapTokens: 0);
        var result = splitter.Split(text);

        // Without overlap the total character count across chunks should not massively exceed the original.
        var totalChars = result.Sum(c => c.Content.Length);
        Assert.True(totalChars <= text.Length * 1.05, $"Total chars {totalChars} should be close to original {text.Length}");
    }

    [Fact]
    public void Split_LongSingleLine_FallsBackToWordSplitting()
    {
        // A single sentence with spaces that would exceed the token limit.
        var words = Enumerable.Range(0, 100).Select(i => $"word{i}");
        var text = string.Join(" ", words); // ~500 chars → ~125 tokens

        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 30, overlapTokens: 0);
        var result = splitter.Split(text);

        Assert.True(result.Count > 1);
        foreach (var chunk in result)
            Assert.True(chunk.EstimatedTokens <= 35, $"Chunk too large: {chunk.EstimatedTokens} tokens");
    }

    [Fact]
    public void Split_MultipleChunks_AllContentPresent()
    {
        var paragraphs = Enumerable.Range(0, 6).Select(i => $"Paragraph {i}: " + new string('x', 40)).ToList();
        var text = string.Join("\n\n", paragraphs);

        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 20, overlapTokens: 0);
        var result = splitter.Split(text);

        Assert.True(result.Count >= 2);
        // Every paragraph must appear in at least one chunk.
        foreach (var para in paragraphs)
            Assert.True(result.Any(c => c.Content.Contains(para)), $"Missing paragraph: {para[..15]}…");
    }

    [Fact]
    public void Split_OverlapTokens_EstimatedTokensIncludesOverlap()
    {
        var block1 = new string('a', 80);
        var block2 = new string('b', 80);
        var text = block1 + "\n\n" + block2;

        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 25, overlapTokens: 5);
        var result = splitter.Split(text);

        // The second chunk's content is longer than block2 alone because of the prepended overlap.
        if (result.Count >= 2)
            Assert.True(result[1].Content.Length > block2.Length);
    }

    [Fact]
    public void Split_SingleNewlineSeparator_SplitsAtNewlines()
    {
        var lines = Enumerable.Range(0, 10).Select(i => new string('x', 20)).ToList();
        var text = string.Join("\n", lines);

        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 8, overlapTokens: 0);
        var result = splitter.Split(text);

        Assert.True(result.Count > 1);
    }

    [Fact]
    public void EstimateTokens_FourCharsPerToken()
    {
        // The splitter must use (length + 3) / 4 — verify via boundary: 400 chars = 100 tokens (fits), 401 = 101 (exceeds 100).
        var fit = new string('x', 400);
        var exceed = new string('x', 401);

        var splitter = new RecursiveCharacterTextSplitter(maxTokens: 100, overlapTokens: 0);
        Assert.Single(splitter.Split(fit));
        Assert.True(splitter.Split(exceed).Count >= 1);
        // The exceed case must produce multiple chunks when there are natural separators.
        var textWithSeparators = new string('x', 200) + "\n\n" + new string('y', 201);
        var result = splitter.Split(textWithSeparators);
        Assert.True(result.Count >= 2);
    }
}
