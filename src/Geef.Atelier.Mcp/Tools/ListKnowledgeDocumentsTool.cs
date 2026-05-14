using System.ComponentModel;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class ListKnowledgeDocumentsTool
{
    [McpServerTool, Description("Lists all documents in the knowledge base. Optionally filter by a tag. Returns id, title, description, tags, chunk count, indexing cost, and creation date for each document.")]
    public static async Task<IReadOnlyList<KnowledgeDocumentDto>> ListKnowledgeDocuments(
        IKnowledgeService knowledgeService,
        [Description("Optional tag to filter documents by. Returns only documents that have this tag.")] string? tag_filter = null,
        CancellationToken cancellationToken = default)
    {
        var documents = await knowledgeService.ListAsync(tag_filter, cancellationToken, KnowledgeScope.Global);
        return documents
            .Select(d => new KnowledgeDocumentDto(
                d.Id,
                d.Title,
                d.Description,
                d.Tags,
                d.ChunkCount,
                d.IndexingCostEur,
                d.CreatedAt))
            .ToList();
    }
}
