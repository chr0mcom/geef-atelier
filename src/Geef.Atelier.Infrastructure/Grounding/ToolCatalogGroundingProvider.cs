using System.Text;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Grounding provider that feeds all registered <see cref="ToolDefinition"/>s as a structured
/// Markdown table into the composition run. The Crew Composer executor uses this context to
/// reference tools by their exact <c>name</c> in <c>tool_names</c> fields instead of
/// hallucinating tool identifiers.
/// </summary>
/// <remarks>
/// Registered as <c>Singleton</c>. <see cref="IToolDefinitionRepository"/> (Scoped) is resolved
/// per invocation via <see cref="IServiceScopeFactory"/> to avoid captive-dependency issues.
/// </remarks>
internal sealed class ToolCatalogGroundingProvider(
    IServiceScopeFactory scopeFactory,
    ILogger<ToolCatalogGroundingProvider> logger) : IGroundingProvider
{
    /// <inheritdoc/>
    public string ProviderType => GroundingProviderTypes.ToolCatalog;

    /// <inheritdoc/>
    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        await using var scope      = scopeFactory.CreateAsyncScope();
        var toolRepo               = scope.ServiceProvider.GetRequiredService<IToolDefinitionRepository>();
        var consultRepo            = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();

        var tools = await toolRepo.GetAllAsync(ct);

        logger.LogInformation(
            "ToolCatalog grounding: run={RunId} toolCount={Count}",
            runId, tools.Count);

        var enrichedContext = BuildContext(tools);

        var citations = tools.Select(t => new SourceCitation(
            Title:             $"Tool: {t.Name} ({t.ToolType})",
            Url:               null,
            Snippet:           Truncate(t.Description, 200),
            DocumentReference: $"tool-catalog://{t.Name}",
            RelevanceScore:    1.0)).ToList();

        var consultation = new GroundingConsultation(
            Id:                    Guid.NewGuid(),
            RunId:                 runId,
            GroundingProviderName: profile.Name,
            Query:                 briefingText,
            Citations:             citations,
            TokensOrCreditsUsed:   0,
            CostEur:               null,
            CreatedAt:             DateTimeOffset.UtcNow);
        await consultRepo.CreateAsync(consultation, ct);

        return new GroundingResult(
            ProviderName:        profile.Name,
            EnrichedContext:     enrichedContext,
            Citations:           citations,
            TokensOrCreditsUsed: 0,
            CostEur:             null,
            ConsultationId:      consultation.Id);
    }

    // ── Context builder ──────────────────────────────────────────────────────

    private static string BuildContext(IReadOnlyList<ToolDefinition> tools)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Available Tool Catalog");
        sb.AppendLine();
        sb.AppendLine("The following tools are registered in the central tool catalogue and may be referenced");
        sb.AppendLine("in `tool_names` when defining actor profiles. Always reference tools by their exact `name`.");
        sb.AppendLine();

        if (tools.Count == 0)
        {
            sb.AppendLine("_No tools registered yet._");
            return sb.ToString();
        }

        sb.AppendLine("| Name | Type | Access | Description |");
        sb.AppendLine("|------|------|--------|-------------|");
        foreach (var t in tools.OrderBy(t => t.Name))
        {
            var access = t.AccessClass == ToolAccessClass.Mutating ? "Mutating ⚠️" : "ReadOnly";
            sb.AppendLine($"| `{t.Name}` | {t.ToolType} | {access} | {EscapeMarkdown(Truncate(t.Description, 120))} |");
        }

        sb.AppendLine();
        sb.AppendLine("> **Note:** Only `ReadOnly` tools may be bound to actors in Phase B. `Mutating` tools require explicit opt-in (Phase C).");
        return sb.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private static string EscapeMarkdown(string text) =>
        text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
