namespace Geef.Atelier.Tests.Mcp;

/// <summary>Verifies that MCP DTOs stay backward-compatible for clients that do not send newer trailing fields.</summary>
public sealed class McpDtoBackwardCompatTests
{
    [Fact]
    public void RunSummaryDto_WithoutArtifactCount_DefaultsToZero()
    {
        // Old clients don't send ArtifactCount — it has a default of 0
        var dto = new Geef.Atelier.Mcp.Models.RunSummaryDto(
            RunId: Guid.NewGuid().ToString(),
            Status: "Completed",
            CreatedAt: DateTimeOffset.UtcNow,
            CreatedByUser: "user");
        // ArtifactCount not specified — should use default 0

        Assert.Equal(0, dto.ArtifactCount);
    }
}
