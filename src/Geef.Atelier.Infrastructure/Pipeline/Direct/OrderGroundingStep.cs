using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>
/// Grounding step of the direct path. Loads the order and its files in an own scope, renders the files into the
/// background document (<see cref="SourceBlockRenderer"/>), reads the constraints from the request in the person's own
/// words and records which file went into the writer's prompt in which form (<c>RunSourceUsages</c>). A file with the
/// role <see cref="SourceRole.TemplateToRevise"/> becomes the seed draft instead of a source, unless the run already
/// carries a seed draft. Warnings of the files become run warnings.
/// </summary>
internal sealed class OrderGroundingStep(
    Guid runId,
    Guid orderId,
    string? seedDraft,
    IServiceScopeFactory scopes,
    SourceOptions options,
    IRunWarningSink? warnings,
    ILogger<OrderGroundingStep> logger) : IGroundingStep
{
    /// <summary>Target of the usages written by this step.</summary>
    public const string WriterTarget = "writer";

    /// <summary>Message of <see cref="DirectWarningCodes.SourcesOverBudget"/>.</summary>
    public const string OverBudgetMessage =
        "Your files are longer than the writer can use completely, so it was given a map to find the parts that matter.";

    public async Task<GroundingResult> RunAsync(string input, CancellationToken cancellationToken)
    {
        Core.Domain.Orders.OrderEntity? order;
        IReadOnlyList<OrderSourceItem> items;
        await using (var scope = scopes.CreateAsyncScope())
        {
            order = await scope.ServiceProvider.GetRequiredService<IOrderRepository>().GetAsync(orderId, cancellationToken);
            items = await scope.ServiceProvider.GetRequiredService<ISourceRepository>()
                .ListForOrderAsync(orderId, includeText: true, cancellationToken);
        }

        if (order is null)
            throw new DirectPathException(DirectPathException.NoOrder);

        var ordered = items.OrderBy(i => i.Link.Position).ToList();
        var template = seedDraft is null
            ? ordered.FirstOrDefault(i => i.Link.Role == SourceRole.TemplateToRevise && !string.IsNullOrWhiteSpace(i.Source.ExtractedText))
            : null;

        var prompt = ordered.ToPromptSources(
            item => logger.LogWarning("Run {RunId}: page selection of source {SourceId} could not be applied; using all pages.", runId, item.Source.Id),
            template);

        var bundle = SourceBlockRenderer.Render(prompt.Sources, options);
        var seed = seedDraft ?? template?.Source.ExtractedText!.WithoutPageMarkers();
        var constraints = RequestConstraintExtractor.Extract(order.RequestText);

        IRunContext context = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, input)
            .Set(AtelierContextKeys.OrderSources, bundle)
            .Set(AtelierContextKeys.OrderConstraints, constraints);
        if (bundle.ContextDocument.Length > 0)
            context = context.Set(AtelierContextKeys.GroundingContext, bundle.ContextDocument);
        if (seed is not null)
            context = context.Set(AtelierContextKeys.SeedDraft, seed);

        await RecordUsagesAsync(bundle, prompt.Positions, template, seed, cancellationToken);
        await ReportWarningsAsync(ordered, bundle, cancellationToken);

        return new GroundingResult
        {
            Context = context,
            Notes = [$"order-sources files={bundle.Sources.Count} chars={bundle.TotalChars} mode={bundle.Mode} template={(template is not null)}"],
        };
    }

    private async Task RecordUsagesAsync(
        OrderSourceBundle bundle, IReadOnlyDictionary<Guid, int> positions, OrderSourceItem? template, string? seed, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var usages = bundle.ToUsages(runId, positions, now);

        if (template is not null && seed is not null)
        {
            usages.Add(new RunSourceUsageEntity
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                SourceId = template.Source.Id,
                FileName = template.Source.FileName,
                Mode = SourceUsageMode.FullText,
                Target = WriterTarget,
                CharsIncluded = seed.Length,
                PagesIncluded = null,
                Position = template.Link.Position,
                CreatedAt = now,
            });
        }

        if (usages.Count == 0)
            return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISourceRepository>().AddRunUsagesAsync(usages.OrderBy(u => u.Position).ToList(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError("Run {RunId}: the used files could not be recorded: {Error}", runId, ex.ToRedactedLogText());
        }
    }

    private async Task ReportWarningsAsync(IReadOnlyList<OrderSourceItem> items, OrderSourceBundle bundle, CancellationToken ct)
    {
        if (warnings is null)
            return;

        if (bundle.TotalChars > options.MapMaxChars)
            await warnings.ReportAsync(runId, RunWarningSources.Grounding, DirectWarningCodes.SourcesOverBudget, OverBudgetMessage, ct: ct);

        foreach (var item in items)
        {
            foreach (var warning in item.Source.Warnings ?? [])
            {
                if (warning.Code == SourceWarningCodes.Duplicate)
                    continue;
                await warnings.ReportAsync(runId, RunWarningSources.Grounding, DirectWarningCodes.SourceWarning,
                    $"{item.Source.FileName}: {warning.Message}", ct: ct);
            }
        }
    }
}
