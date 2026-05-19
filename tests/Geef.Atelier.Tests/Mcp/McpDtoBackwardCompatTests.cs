using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// Verifies that ProposedTemplateDto and ProposedProfileDto remain backward-compatible:
/// existing MCP clients that don't send the new finalizer fields must still deserialize
/// successfully (all new fields are trailing-optional with defaults).
/// </summary>
public sealed class McpDtoBackwardCompatTests
{
    [Fact]
    public void ProposedTemplateDto_WithoutFinalizerFields_UsesDefaults()
    {
        // Simulate old MCP client output without finalizer fields
        var dto = new ProposedTemplateDto(
            Name: "test-template",
            DisplayName: "Test Template",
            Description: "desc",
            ExecutorProfileName: "default-executor",
            ReviewerProfileNames: ["briefing-fidelity"],
            AdvisorProfileNames: [],
            GroundingProviderProfileNames: [],
            EvaluationStrategy: "Parallel");
        // No FinalizerProfileNames, RunFinalizersOnMaxAttempts, FinalizerReasoning

        Assert.Null(dto.FinalizerProfileNames);
        Assert.False(dto.RunFinalizersOnMaxAttempts);
        Assert.Null(dto.FinalizerReasoning);
    }

    [Fact]
    public void ProposedProfileDto_WithoutFinalizerFields_UsesDefaults()
    {
        var dto = new ProposedProfileDto(
            ProfileType: "reviewer",
            Name: "my-reviewer",
            DisplayName: "My Reviewer",
            Description: "desc",
            Model: "gpt-5.5",
            Provider: "codex-cli",
            SystemPrompt: "You review content.",
            MaxTokens: null,
            ReviewerFocus: null,
            AdvisorMode: null,
            AdvisorTrigger: null,
            GroundingProviderType: null,
            GroundingProviderSettings: null);
        // No FinalizerType, FinalizerSettings, FinalizerReasoning

        Assert.Null(dto.FinalizerType);
        Assert.Null(dto.FinalizerSettings);
        Assert.Null(dto.FinalizerReasoning);
    }

    [Fact]
    public void ProposedTemplateDto_WithFinalizerFields_RoundTrips()
    {
        var dto = new ProposedTemplateDto(
            Name: "export-template",
            DisplayName: "Export Template",
            Description: "desc",
            ExecutorProfileName: "default-executor",
            ReviewerProfileNames: [],
            AdvisorProfileNames: [],
            GroundingProviderProfileNames: [],
            EvaluationStrategy: "Parallel",
            FinalizerProfileNames: ["export-markdown", "anti-ai-voice"],
            RunFinalizersOnMaxAttempts: true,
            FinalizerReasoning: "Export is required for delivery.");

        Assert.Equal(2, dto.FinalizerProfileNames!.Count);
        Assert.Contains("export-markdown", dto.FinalizerProfileNames);
        Assert.True(dto.RunFinalizersOnMaxAttempts);
        Assert.Equal("Export is required for delivery.", dto.FinalizerReasoning);
    }

    [Fact]
    public void ProposedProfileDto_FinalizerType_WithTransform_IsCorrect()
    {
        var dto = new ProposedProfileDto(
            ProfileType: "finalizer",
            Name: "custom-anti-ai",
            DisplayName: "Custom Anti-AI Voice",
            Description: "Polish text",
            Model: "gpt-5.5",
            Provider: "codex-cli",
            SystemPrompt: "Remove AI patterns.",
            MaxTokens: 4096,
            ReviewerFocus: null,
            AdvisorMode: null,
            AdvisorTrigger: null,
            GroundingProviderType: null,
            GroundingProviderSettings: null,
            FinalizerType: "Transform",
            FinalizerSettings: new Dictionary<string, string> { ["Format"] = "markdown" });

        Assert.Equal("finalizer", dto.ProfileType);
        Assert.Equal("Transform", dto.FinalizerType);
        Assert.NotNull(dto.FinalizerSettings);
    }

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
