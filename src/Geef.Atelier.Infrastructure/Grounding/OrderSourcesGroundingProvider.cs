using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Grounding provider of the careful mode for orders with files: renders all files of the run's order in full text
/// (the same source block as the direct path, <see cref="SourceBlockRenderer"/>) as enriched context, records the
/// usages (<c>RunSourceUsages</c>, target <c>writer</c>) and one consultation listing the files. Runs without an order
/// or without files get no context.
/// </summary>
internal sealed class OrderSourcesGroundingProvider(
    IServiceScopeFactory scopeFactory,
    IOptions<SourceOptions> sourceOptions,
    ILogger<OrderSourcesGroundingProvider> logger) : IGroundingProvider
{
    private readonly SourceOptions _options = sourceOptions.Value;

    /// <inheritdoc/>
    public string ProviderType => GroundingProviderTypes.OrderSources;

    /// <inheritdoc/>
    public async Task<GroundingResult> EnrichAsync(string briefingText, GroundingProviderProfile profile, Guid runId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var sources = scope.ServiceProvider.GetRequiredService<ISourceRepository>();

        var link = await orders.GetLinkForRunAsync(runId, ct);
        var items = link is { } l ? await sources.ListForOrderAsync(l.OrderId, includeText: true, ct) : [];

        var prompt = items.ToPromptSources(item => logger.LogWarning(
            "Run {RunId}: page selection of source {SourceId} could not be applied; using all pages.", runId, item.Source.Id));
        var bundle = SourceBlockRenderer.Render(prompt.Sources, _options);
        var citations = bundle.Sources
            .Select(s => new SourceCitation(s.FileName, null, $"{s.Characters} characters", $"source://{s.SourceId}", null))
            .ToList();

        if (bundle.Sources.Count > 0)
            await sources.AddRunUsagesAsync(bundle.ToUsages(runId, prompt.Positions, DateTimeOffset.UtcNow), ct);

        var consultation = new GroundingConsultation(
            Id: Guid.NewGuid(),
            RunId: runId,
            GroundingProviderName: profile.Name,
            Query: string.Empty,
            Citations: citations,
            TokensOrCreditsUsed: 0,
            CostEur: null,
            CreatedAt: DateTimeOffset.UtcNow);
        await scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>().CreateAsync(consultation, ct);

        return new GroundingResult(
            ProviderName: profile.Name,
            EnrichedContext: bundle.ContextDocument,
            Citations: citations,
            TokensOrCreditsUsed: 0,
            CostEur: 0m,
            ConsultationId: consultation.Id);
    }
}
