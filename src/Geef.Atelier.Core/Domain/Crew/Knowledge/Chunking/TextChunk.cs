namespace Geef.Atelier.Core.Domain.Crew.Knowledge.Chunking;

/// <summary>
/// A single text segment produced by <see cref="RecursiveCharacterTextSplitter"/>.
/// </summary>
/// <param name="Index">Zero-based position of this chunk in the splitting output sequence.</param>
/// <param name="Content">Text content of the chunk, possibly prefixed with overlap from the preceding chunk.</param>
/// <param name="EstimatedTokens">Token count estimated at ~4 characters per token.</param>
public sealed record TextChunk(int Index, string Content, int EstimatedTokens);
