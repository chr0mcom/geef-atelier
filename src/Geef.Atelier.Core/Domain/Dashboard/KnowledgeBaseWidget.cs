namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Knowledge-base stats: document count, chunks, size, most-used files.</summary>
public sealed record KnowledgeBaseStats(
    int DocumentCount,
    int ChunkCount,
    long TotalBytes,
    IReadOnlyList<KbFileRef> TopFiles);

public sealed record KbFileRef(Guid DocumentId, string FileName, int ChunkCount, int? ReferenceCount);
