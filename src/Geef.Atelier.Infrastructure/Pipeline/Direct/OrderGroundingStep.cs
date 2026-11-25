using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Runs;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>
/// Grounding step of the direct path. Loads the order and its files in an own scope, renders the files into the
/// background document (<see cref="SourceBlockRenderer"/>), reads the constraints from the request in the person's own
/// words and records which file went into the writer's prompt in which form (<c>RunSourceUsages</c>). A file with the
/// role <see cref="SourceRole.TemplateToRevise"/> becomes the seed draft instead of a source, unless the run already
/// carries a seed draft. Warnings of the files become run warnings. For the checks of stage 3 it builds the material of the
/// fact check (<see cref="AtelierContextKeys.FactMaterial"/>), fixes the fact-check mode of the run for every run alike (never by
/// difficulty, length or count) and stores it, and recalculates the totals in the files when <see cref="DirectOptions.SourceArithmetic"/>
/// is on. <see cref="DirectOptions"/> and <see cref="IRunFactCheckModeStore"/> come from the scope.
/// </summary>
internal sealed class OrderGroundingStep(
    Guid runId,
    Guid orderId,
    string? seedDraft,
    IServiceScopeFactory scopes,
    SourceOptions options,
    IRunWarningSink warnings,
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
        var templateText = seedDraft is null ? template?.Source.ExtractedText!.WithoutPageMarkers() : null;
        var seed = seedDraft ?? templateText;
        var constraints = Constraints(order);

        var direct = Direct();
        var files = FactFiles(prompt.Sources, template, templateText);
        var material = Material(input, files, direct);
        var mode = Mode(direct, order.CreatedByUser);
        var arithmetic = direct.SourceArithmetic ? SourceArithmeticCheck.Check(files.Select(f => (f.Id, f.FileName, f.Text)).ToList()) : null;

        IRunContext context = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, input)
            .Set(AtelierContextKeys.OrderSources, bundle)
            .Set(AtelierContextKeys.OrderConstraints, constraints)
            .Set(AtelierContextKeys.FactMaterial, material)
            .Set(AtelierContextKeys.FactCheckMode, mode);
        if (bundle.ContextDocument.Length > 0)
            context = context.Set(AtelierContextKeys.GroundingContext, bundle.ContextDocument);
        if (seed is not null)
            context = context.Set(AtelierContextKeys.SeedDraft, WriterNotesFormat.Defuse(seed));
        if (arithmetic is not null)
            context = context.Set(AtelierContextKeys.SourceArithmetic, arithmetic);

        await StoreModeAsync(mode, cancellationToken);
        await RecordUsagesAsync(bundle, prompt.Positions, template, seed, cancellationToken);
        await ReportWarningsAsync(ordered, bundle, cancellationToken);

        return new GroundingResult
        {
            Context = context,
            Notes =
            [
                $"order-sources files={bundle.Sources.Count} chars={bundle.TotalChars} mode={bundle.Mode} template={(template is not null)}",
                $"fact-check mode={mode} scope={material.Scope} material_chars={material.TotalChars}"
                    + (arithmetic is null ? string.Empty : $" totals={arithmetic.TotalsFound} totals_off={arithmetic.Issues.Count}"),
            ],
        };
    }

    /// <summary>Id of the text to revise in the material of the fact check.</summary>
    public const string TemplateSourceId = "S0";

    /// <summary>
    /// The files of the material: the files given to the writer in their order, and before them the text to revise as
    /// <see cref="TemplateSourceId"/> when the run revises a file of the order (never an earlier version of the text itself).
    /// </summary>
    internal static IReadOnlyList<FactSource> FactFiles(IReadOnlyList<PromptSource> sources, OrderSourceItem? template, string? templateText)
    {
        var files = new List<FactSource>(sources.Count + 1);
        if (template is not null && templateText is not null)
            files.Add(new FactSource(TemplateSourceId, template.Source.FileName, templateText));
        files.AddRange(sources.Select(s => new FactSource(s.Id, s.FileName, s.Text)));
        return files;
    }

    /// <summary>
    /// Everything the text may rely on: request, answers of the person and of the assistant and the profile, read back from the
    /// frozen briefing (<see cref="BriefingSections.Parse"/>), and the files. Assumptions and the writer's notes are never part of it.
    /// Above <see cref="DirectOptions.FactCheckMaxMaterialChars"/> the scope is <see cref="FactCheckScope.WithoutFiles"/>; the file map
    /// is always there, so that the check can also narrow the scope once it knows the length of the text.
    /// </summary>
    internal static FactMaterial Material(string briefing, IReadOnlyList<FactSource> files, DirectOptions options)
    {
        var parts = BriefingSections.Parse(briefing);
        var map = files.Count == 0 ? null : string.Join('\n', files.Select(f => $"{f.Id} \"{f.FileName.Replace('"', '\'')}\" — {f.Text.Length} characters"));
        var full = new FactMaterial(parts.Request, parts.PersonAnswers, parts.AssistantAnswers, parts.Profile, files, FactCheckScope.Full, map);
        return full.TotalChars > options.FactCheckMaxMaterialChars ? full with { Scope = FactCheckScope.WithoutFiles } : full;
    }

    /// <summary>The fact-check mode of the run: the override of the person who ordered the text, otherwise the global switch. Every run gets one.</summary>
    internal static FactCheckMode Mode(DirectOptions options, string? user) =>
        user is not null && options.FactCheckModeOverrides is { } overrides && overrides.TryGetValue(user, out var mode) ? mode : options.FactCheckMode;

    private DirectOptions Direct()
    {
        using var scope = scopes.CreateScope();
        return scope.ServiceProvider.GetService<IOptions<DirectOptions>>()?.Value ?? new DirectOptions();
    }

    private async Task StoreModeAsync(FactCheckMode mode, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (scope.ServiceProvider.GetService<IRunFactCheckModeStore>() is { } store)
                await store.SetAsync(runId, mode, ct);
            else
                logger.LogWarning("Run {RunId}: no store for the fact-check mode is registered; mode {Mode} was not recorded.", runId, mode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError("Run {RunId}: the fact-check mode could not be recorded: {Error}", runId, ex.ToRedactedLogText());
        }
    }

    /// <summary>
    /// The constraints read from the request in the person's own words; for a clarified order with answers, the answered length
    /// is added when the request names none (<see cref="IntakeConstraints.WithAnswers"/>). The text parser never sees the answers.
    /// </summary>
    internal static OrderConstraints Constraints(Core.Domain.Orders.OrderEntity order)
    {
        var fromRequest = RequestConstraintExtractor.Extract(order.RequestText);
        if (order.ClarifyJson is null || order.AnswersJson is null)
            return fromRequest;
        return IntakeConstraints.WithAnswers(fromRequest, IntakeRecordJson.TryParse(order.ClarifyJson), IntakeRecordJson.ParseAnswers(order.AnswersJson));
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
