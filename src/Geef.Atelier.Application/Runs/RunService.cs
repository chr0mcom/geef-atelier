using System.Globalization;
using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Runs;

internal sealed class RunService(
    IRunPersistenceService                persistence,
    IRunRepository                        repository,
    ICrewService                          crewService,
    IAdvisorConsultationRepository        consultationRepository,
    IKnowledgeService                     knowledgeService,
    ILogger<RunService>                   logger,
    IRunWarningSink                       warningSink,
    IRunWarningRepository                 warningRepository,
    IGroundingConsultationRepository?     groundingConsultationRepository = null,
    IToolInvocationRepository?            toolInvocationRepository = null,
    IOrderRepository?                     orderRepository = null,
    IOptions<OrderOptions>?               orderOptions = null) : IRunService
{
    internal const string AttachmentUploadFailedMessage = "The attached files could not be prepared. Please submit the task again.";

    private static readonly JsonSerializerOptions SnapshotJsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc/>
    public async Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BriefingText))
            throw new ArgumentException("Briefing text must not be empty.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.ConfigJson);

        var normalizedConfig = string.IsNullOrEmpty(request.ConfigJson) ? "{}" : request.ConfigJson;
        if (normalizedConfig != "{}")
        {
            try { using var _ = JsonDocument.Parse(normalizedConfig); }
            catch (JsonException ex)
            { throw new ArgumentException("configJson must be valid JSON.", nameof(request), ex); }
        }

        // Defensive recursion guard: AutoCompose must never be set on a CrewComposition run.
        // The CrewMaterializeFinalizerExecutor always submits with Kind=Standard and AutoCompose=false;
        // this guard prevents accidental infinite loops if the caller gets the flags wrong.
        if (request.Kind == RunKind.CrewComposition && request.AutoCompose)
            throw new InvalidOperationException("AutoCompose cannot be used inside a CrewComposition run.");

        // When AutoCompose is requested, redirect to the fixed composition crew and embed the
        // original task + chain flag into ConfigJson so the materializer can act on them later.
        string effectiveBriefing        = request.BriefingText;
        string effectiveTemplateName    = request.CrewTemplateName ?? string.Empty;
        RunKind effectiveKind           = request.Kind;

        if (request.AutoCompose)
        {
            effectiveKind        = RunKind.CrewComposition;
            effectiveTemplateName = "crew-composer";
            effectiveBriefing    = $"Compose a crew for the following task:\n\n{request.BriefingText}";

            // Embed the original task and chain flag into the config JSON so the materializer
            // can retrieve them when the composition run completes.
            using var configDoc  = JsonDocument.Parse(normalizedConfig);
            var configDict       = new Dictionary<string, JsonElement>(
                configDoc.RootElement.EnumerateObject()
                    .Select(p => new KeyValuePair<string, JsonElement>(p.Name, p.Value)));

            var writerOpts = new JsonWriterOptions { Indented = false };
            using var ms   = new System.IO.MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, writerOpts))
            {
                writer.WriteStartObject();
                foreach (var (key, val) in configDict)
                {
                    writer.WritePropertyName(key);
                    val.WriteTo(writer);
                }
                writer.WriteString("original_task", request.BriefingText);
                writer.WriteBoolean("chain_to_task_run", request.ChainToTaskRun);
                writer.WriteEndObject();
            }
            normalizedConfig = System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }

        var resolved = await crewService.ResolveSnapshotWithWarningsAsync(
            string.IsNullOrEmpty(effectiveTemplateName) ? null : effectiveTemplateName,
            request.AutoCompose ? null : request.CustomCrew,
            cancellationToken);
        var snapshot = resolved.Snapshot;
        var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOpts);
        // CrewTemplateName is the template name from snapshot (null for inline spec)
        var resolvedTemplateName = snapshot.TemplateName;
        var hasAttachments = request.Attachments is { Count: > 0 };

        var runId = await persistence.CreateRunAsync(
            effectiveBriefing, normalizedConfig, request.CreatedByUser,
            resolvedTemplateName, hasAttachments ? null : snapshotJson, effectiveKind,
            request.ParentCompositionRunId, cancellationToken);

        if (effectiveKind != RunKind.Learning)
            await LinkToOrderAsync(runId, request.Entry, cancellationToken);

        await ReportAssignmentWarningsAsync(runId, resolved.Warnings);

        if (request.Attachments is { Count: > 0 } attachments)
        {
            try
            {
                foreach (var attachment in attachments)
                {
                    await knowledgeService.UploadRunAttachmentAsync(
                        runId,
                        attachment.Filename,
                        new MemoryStream(attachment.Content),
                        attachment.Filename,
                        attachment.ContentType,
                        cancellationToken);
                }

                var existing = snapshot.GroundingProviders ?? [];
                var extendedProviders = existing.Any(p => p.Name == SystemCrew.RunAttachmentsProfile.Name)
                    ? existing.ToList()
                    : [SystemCrew.RunAttachmentsProfile, .. existing];

                var extendedSnapshot = snapshot with { GroundingProviders = extendedProviders };
                var extendedSnapshotJson = JsonSerializer.Serialize(extendedSnapshot, SnapshotJsonOpts);
                await persistence.UpdateSnapshotAsync(runId, extendedSnapshotJson, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError("Attachment upload failed for run {RunId}; marking run as Failed: {Error}", runId, ex.ToRedactedLogText());
                await persistence.MarkRunFailedAsync(runId, AttachmentUploadFailedMessage, cancellationToken);
                throw;
            }
        }

        return runId;
    }

    private async Task ReportAssignmentWarningsAsync(Guid runId, IReadOnlyList<Crew.Models.ModelAssignmentWarning> warnings)
    {
        foreach (var warning in warnings)
            await warningSink.ReportAsync(runId, RunWarningSources.ModelAssignment, RunWarningCodes.ModelAssignment,
                $"The AI model for \"{warning.ShownName}\" could not be chosen as planned, so a different one was used.");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RunWarningEntity>> ListRunWarningsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        if (requestingUsername is not null && await GetRunAsync(runId, requestingUsername, cancellationToken) is null)
            return [];
        return await warningRepository.ListByRunAsync(runId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        var run = await repository.GetByIdAsync(runId, cancellationToken);
        if (run is null) return null;
        if (requestingUsername is not null && run.CreatedByUser != requestingUsername) return null;
        return run;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        return repository.ListAsync(limit, statusFilter, requestingUsername, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        if (requestingUsername is not null)
        {
            var run = await repository.GetByIdAsync(runId, cancellationToken);
            if (run is null || run.CreatedByUser != requestingUsername) return false;
        }
        return await repository.RequestCancellationAsync(runId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        var details = await repository.GetDetailsAsync(runId, cancellationToken);
        if (details is null) return null;
        if (requestingUsername is not null && details.Run.CreatedByUser != requestingUsername) return null;
        return details;
    }

    /// <inheritdoc/>
    public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default)
        => repository.GetWelcomeStatsAsync(requestingUsername, cancellationToken);

    /// <inheritdoc/>
    public async Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var details = await repository.GetDetailsAsync(options.ParentRunId, cancellationToken);
        if (details is null)
            throw new InvalidOperationException($"Run {options.ParentRunId} not found.");

        if (requestingUsername is not null && details.Run.CreatedByUser != requestingUsername)
            throw new InvalidOperationException(
                $"Run {options.ParentRunId} does not belong to user {requestingUsername}.");

        if (details.Run.Status is not (RunStatus.Aborted or RunStatus.Failed))
            throw new InvalidOperationException(
                $"Run {options.ParentRunId} is in status {details.Run.Status} and cannot be resumed. Only Aborted and Failed runs are resumable.");

        if (details.Run.ResumeRefusal() is { } refusal)
            throw new RunNotResumableException(refusal);

        if (!CrewSnapshot.IsUsableForRun(details.Run.CrewSnapshot))
            throw new RunNotResumableException(RunNotResumableException.NotPreparedMessage);

        string? seedDraftText = null;
        if (options.UseSeedDraft && !string.IsNullOrWhiteSpace(details.Run.FinalText))
        {
            seedDraftText = details.Run.FinalText;
        }
        else if (options.UseSeedDraft && details.Iterations.Count > 0)
        {
            seedDraftText = details.Iterations
                .OrderByDescending(i => i.Iteration.IterationNumber)
                .First()
                .Iteration.ArtifactText;
        }

        var snapshotJson = details.Run.CrewSnapshot;
        if (options.MaxIterationsOverride.HasValue && !string.IsNullOrEmpty(snapshotJson))
        {
            var snapshot = CrewSnapshot.Deserialize(snapshotJson);
            if (snapshot is not null)
            {
                var patched = snapshot with
                {
                    ConvergenceOverride = (snapshot.ConvergenceOverride ?? new ConvergencePolicyOverride(null, null, null, null))
                        with { MaxIterations = options.MaxIterationsOverride }
                };
                snapshotJson = JsonSerializer.Serialize(patched, SnapshotJsonOpts);
            }
        }

        if (details.Run.ChangesAnEarlierVersion() && details.Run.OrderId is { } changedOrderId && orderRepository is not null)
            return await ResumeChangeAsync(details.Run, changedOrderId, snapshotJson, cancellationToken);

        var resumedId = await persistence.CreateResumedRunAsync(
            details.Run.BriefingText,
            details.Run.ConfigJson,
            details.Run.CreatedByUser,
            details.Run.CrewTemplateName,
            snapshotJson,
            options.ParentRunId,
            seedDraftText,
            details.Run.Kind,
            cancellationToken);

        if (details.Run.Kind != RunKind.Learning)
            await LinkToOrderAsync(resumedId, OrderEntry.Legacy, cancellationToken);

        return resumedId;
    }

    private async Task<Guid> ResumeChangeAsync(RunEntity failed, Guid orderId, string? snapshotJson, CancellationToken cancellationToken)
    {
        var orderRuns = await orderRepository!.ListRunsAsync(orderId, cancellationToken);
        var shown = OrderStateResolver.SelectShownRun(orderRuns)
                    ?? throw new RunNotResumableException(RevisionLines.NothingToChange);
        if ((shown.OrderVersion ?? 0) > (failed.OrderVersion ?? 0))
            throw new RunNotResumableException(RevisionLines.ResumeLaterVersion);
        var version = orderRuns.Where(r => r.Kind == RunKind.Standard).Max(r => r.OrderVersion ?? 0) + 1;
        var change = RevisionBrief.WithoutRetired(failed.RevisionHint);
        if (change.Length == 0)
            throw new RunNotResumableException(RevisionLines.ResumeRetired);
        var run = new RunEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Pending,
            BriefingText = RevisionBrief.Compose(shown.BriefingText ?? string.Empty, change, version),
            ConfigJson = failed.ConfigJson,
            CreatedByUser = failed.CreatedByUser,
            CrewTemplateName = failed.CrewTemplateName,
            CrewSnapshot = snapshotJson,
            TokensTotal = 0,
            CostTotal = 0m,
            Kind = RunKind.Standard,
            OrderId = orderId,
            OrderVersion = version,
            ParentRunId = shown.Id,
            SeedDraftText = shown.FinalText,
            RevisionHint = change,
        };

        var maxVersions = (orderOptions?.Value ?? new OrderOptions()).MaxVersionsPerOrder;
        var (result, created) = await orderRepository.TryAddRevisionRunAsync(orderId, shown.Id, run, maxVersions, cancellationToken);
        logger.LogInformation("Run {RunId} resumed as version {Version}, a change of version {BaseVersion} of order {OrderId}: {Result}.",
            failed.Id, created, shown.OrderVersion, orderId, result);
        return result switch
        {
            RevisionStart.Created => run.Id,
            RevisionStart.Busy or RevisionStart.BaseNotShown => throw new RunNotResumableException(RevisionLines.Busy),
            RevisionStart.TooManyVersions => throw new RunNotResumableException(
                string.Format(CultureInfo.InvariantCulture, RevisionLines.TooManyVersionsFormat, maxVersions)),
            RevisionStart.Cancelled => throw new RunNotResumableException(RevisionLines.Stopped),
            _ => throw new RunNotResumableException(RevisionLines.NotFound),
        };
    }

    private async Task LinkToOrderAsync(Guid runId, OrderEntry entry, CancellationToken cancellationToken)
    {
        if (orderRepository is null)
            return;
        try
        {
            await orderRepository.LinkLegacyRunAsync(runId, entry, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError("Run {RunId} could not be linked to an order; it runs without one: {Error}", runId, ex.ToRedactedLogText());
        }
    }

    /// <inheritdoc/>
    public async Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        var details = await repository.GetDetailsAsync(runId, cancellationToken);
        if (details is null)
            return null;
        if (requestingUsername is not null && details.Run.CreatedByUser != requestingUsername)
            return null;

        var snapshot = details.Run.CrewSnapshot is { } s ? CrewSnapshot.Deserialize(s) : null;
        var groundedBrief = details.Run.BriefingText;

        var consultations = await consultationRepository.GetByRunIdAsync(runId, cancellationToken);

        // Build a name→trigger lookup from the snapshot's advisor profiles (null-safe).
        var triggerDict = snapshot is not null
            ? snapshot.Advisors.ToDictionary(a => a.Name, a => a.Trigger, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, AdvisorTrigger>(StringComparer.OrdinalIgnoreCase);

        var recoveryAdvisors = consultations
            .Where(c => c.IterationNumber == -1)
            .ToList();

        var nonRecovery = consultations
            .Where(c => c.IterationNumber != -1)
            .ToList();

        var groundingAdvisors = nonRecovery
            .Where(c => triggerDict.TryGetValue(c.AdvisorProfileName, out var t)
                        && t == AdvisorTrigger.BeforeFirstExecution)
            .ToList();

        // This covers BeforeEveryExecution as well as fallback for profiles not found in the snapshot.
        var iterationSet = new HashSet<Guid>(groundingAdvisors.Select(c => c.Id));
        var advisorsByIteration = nonRecovery
            .Where(c => !iterationSet.Contains(c.Id))
            .ToLookup(c => c.IterationNumber);

        IReadOnlyList<GroundingConsultation> groundingConsultations = groundingConsultationRepository is not null
            ? await groundingConsultationRepository.GetByRunIdAsync(runId, cancellationToken)
            : [];

        IReadOnlyList<ToolInvocation> toolInvocations = toolInvocationRepository is not null
            ? await toolInvocationRepository.GetByRunIdAsync(runId, cancellationToken)
            : [];

        IReadOnlyList<RunWarningEntity> warnings = warningRepository is not null
            ? await warningRepository.ListByRunAsync(runId, cancellationToken)
            : [];

        return new RunWithGroundingViewModel(
            details,
            snapshot,
            groundedBrief,
            groundingAdvisors,
            recoveryAdvisors,
            advisorsByIteration,
            groundingConsultations,
            toolInvocations,
            warnings);
    }

    /// <inheritdoc/>
    public async Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        var run = await GetRunAsync(runId, requestingUsername, cancellationToken);
        if (run is null) return false;

        if (run.Status is RunStatus.Pending or RunStatus.Running)
            throw new InvalidOperationException("Running or pending runs cannot be deleted. Cancel the run first.");

        var link = orderRepository is null ? null : await orderRepository.GetLinkForRunAsync(runId, cancellationToken);
        await repository.DeleteAsync(runId, cancellationToken);

        if (link is { OrderId: var orderId } && (await orderRepository!.ListRunsAsync(orderId, cancellationToken)).Count == 0)
            await orderRepository.DeleteAsync(orderId, cancellationToken);
        return true;
    }
}
