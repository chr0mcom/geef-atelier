using System.Text;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Grounding provider that feeds the reusable specialization-pack catalogue (General + DomainScoped;
/// foreign TaskBound packs are excluded) as a structured Markdown table into the composition run, so
/// the Crew Composer references existing packs by their exact <c>name</c> in <c>pack_names</c> instead
/// of inventing new ones.
/// </summary>
internal sealed class PackCatalogGroundingProvider(
    IServiceScopeFactory scopeFactory,
    ILogger<PackCatalogGroundingProvider> logger) : IGroundingProvider
{
    /// <inheritdoc/>
    public string ProviderType => GroundingProviderTypes.PackCatalog;

    /// <inheritdoc/>
    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var packRepo    = scope.ServiceProvider.GetRequiredService<ISpecializationPackRepository>();
        var consultRepo = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();

        // The composer is building a brand-new crew: no owning crew yet, so only reusable packs
        // (General + DomainScoped) are offered. Foreign TaskBound packs are never reusable.
        var packs = (await packRepo.ListAsync(includeSystem: true, ct))
            .Where(p => !p.Archived && p.Scope != PackScope.TaskBound)
            .OrderBy(p => p.Name)
            .ToList();

        logger.LogInformation("PackCatalog grounding: run={RunId} packCount={Count}", runId, packs.Count);

        var enrichedContext = BuildContext(packs);

        var citations = packs.Select(p => new SourceCitation(
            Title:             $"Pack: {p.Name} ({p.Scope})",
            Url:               null,
            Snippet:           Truncate(p.Description, 200),
            DocumentReference: $"pack-catalog://{p.Name}",
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

    private static string BuildContext(IReadOnlyList<SpecializationPack> packs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Available Specialization Pack Catalog");
        sb.AppendLine();
        sb.AppendLine("Reusable packs that may be referenced in an actor's `pack_names`. Prefer reusing");
        sb.AppendLine("these over defining new packs. Reference packs by their exact `name`.");
        sb.AppendLine();

        if (packs.Count == 0)
        {
            sb.AppendLine("_No reusable packs registered yet._");
            return sb.ToString();
        }

        sb.AppendLine("| Name | Scope | Domain | Actor types | Description |");
        sb.AppendLine("|------|-------|--------|-------------|-------------|");
        foreach (var p in packs)
        {
            var actors = string.Join(", ", p.ApplicableActorTypes);
            sb.AppendLine($"| `{p.Name}` | {p.Scope} | {p.Domain ?? "—"} | {actors} | {EscapeMarkdown(Truncate(p.Description, 120))} |");
        }

        sb.AppendLine();
        sb.AppendLine("> **Note:** New packs you define default to `TaskBound` (bound to this crew). DomainScoped");
        sb.AppendLine("> packs are only reusable in crews of the same domain.");
        return sb.ToString();
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";

    private static string EscapeMarkdown(string text) =>
        text.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
