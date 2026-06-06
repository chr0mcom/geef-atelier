using System.Text.Json;
using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Finalizers;

/// <summary>
/// Finalizer that materializes the Crew-Spec JSON produced by a composition run into real
/// database entities (profiles + crew template), then optionally chains a follow-up task run.
/// Only executes for <see cref="RunKind.CrewComposition"/> runs; returns immediately for all others.
/// </summary>
internal sealed class CrewMaterializeFinalizerExecutor(
    IServiceScopeFactory scopeFactory,
    ILogger<CrewMaterializeFinalizerExecutor> logger) : IFinalizerExecutor
{
    /// <inheritdoc/>
    public FinalizerType Type => FinalizerType.CrewMaterialize;

    /// <inheritdoc/>
    public async Task<FinalizerExecutionResult> ExecuteAsync(
        FinalizerProfile profile,
        FinalizerExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var runRepo    = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            var materializer = scope.ServiceProvider.GetRequiredService<ICrewMaterializer>();
            var runService = scope.ServiceProvider.GetRequiredService<IRunService>();

            // ── Recursion guard: only act on composition runs ─────────────────────
            var run = await runRepo.GetByIdAsync(context.RunId, cancellationToken);
            if (run is null)
            {
                logger.LogWarning(
                    "CrewMaterialize: run {RunId} not found; skipping.", context.RunId);
                return Ok(profile.Name);
            }

            if (run.Kind != RunKind.CrewComposition)
            {
                logger.LogDebug(
                    "CrewMaterialize: run {RunId} is kind {Kind}; only CrewComposition runs are processed.",
                    context.RunId, run.Kind);
                return Ok(profile.Name);
            }

            // ── Obtain the Crew-Spec JSON from the finalizer context ──────────────
            // The composition pipeline stores the tool-call output as the FinalText of the run.
            var specJson = context.FinalText;
            if (string.IsNullOrWhiteSpace(specJson))
            {
                logger.LogWarning(
                    "CrewMaterialize: run {RunId} has no final text (spec JSON); skipping materialization.",
                    context.RunId);
                return Ok(profile.Name);
            }

            // ── Materialize ───────────────────────────────────────────────────────
            MaterializeCrewResult result;
            try
            {
                result = await materializer.MaterializeAsync(specJson, context.RunId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "CrewMaterialize: materialization failed for run {RunId}", context.RunId);
                return Error(context.RunId, profile.Name, $"Materialization failed: {ex.Message}");
            }

            logger.LogInformation(
                "CrewMaterialize: run {RunId} — template '{Template}', wasDuplicate={Dup}, warnings={WarnCount}",
                context.RunId, result.TemplateName, result.WasDuplicate, result.Warnings.Count);

            // ── Read ConfigJson for original_task + chain_to_task_run ─────────────
            var (originalTask, chainToTaskRun) = ReadCompositionConfig(run.ConfigJson, context.RunId);

            // ── Optionally chain a follow-up task run (fire-and-forget) ──────────
            if (chainToTaskRun && !string.IsNullOrWhiteSpace(originalTask) && !string.IsNullOrWhiteSpace(result.TemplateName))
            {
                try
                {
                    var taskRunId = await runService.SubmitRunAsync(new SubmitRunRequest(
                        BriefingText:           originalTask,
                        ConfigJson:             "{}",
                        CreatedByUser:          run.CreatedByUser,
                        CrewTemplateName:       result.TemplateName,
                        ParentCompositionRunId: context.RunId,
                        Kind:                   RunKind.Standard),
                        CancellationToken.None);

                    logger.LogInformation(
                        "CrewMaterialize: run {RunId} — chained task run {TaskRunId} using template '{Template}'",
                        context.RunId, taskRunId, result.TemplateName);
                }
                catch (Exception ex)
                {
                    // A submit failure must not fail the composition run — the crew was created
                    // successfully — but it MUST be visible: surface it as a Status artifact so the
                    // user sees why no follow-up task run started instead of a silent "Completed".
                    logger.LogError(ex,
                        "CrewMaterialize: failed to submit chained task run for composition run {RunId}", context.RunId);
                    return Error(context.RunId, profile.Name,
                        $"Crew '{result.TemplateName}' was created, but the chained task run could not be started: {ex.Message}");
                }
            }
            else if (!chainToTaskRun)
            {
                logger.LogDebug(
                    "CrewMaterialize: run {RunId} — chain_to_task_run is false; no follow-up run submitted.",
                    context.RunId);
            }

            return Ok(profile.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CrewMaterialize: unexpected error for run {RunId}", context.RunId);
            return Error(context.RunId, profile.Name, $"Unexpected error: {ex.Message}");
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>Reads <c>original_task</c> and <c>chain_to_task_run</c> from the run's ConfigJson.</summary>
    private (string? OriginalTask, bool ChainToTaskRun) ReadCompositionConfig(string configJson, Guid runId)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return (null, true);
        try
        {
            using var doc = JsonDocument.Parse(configJson);
            var root = doc.RootElement;

            string? originalTask = null;
            if (root.TryGetProperty("original_task", out var taskEl) &&
                taskEl.ValueKind == JsonValueKind.String)
                originalTask = taskEl.GetString();

            bool chainToTaskRun = true;
            if (root.TryGetProperty("chain_to_task_run", out var chainEl) &&
                (chainEl.ValueKind == JsonValueKind.True || chainEl.ValueKind == JsonValueKind.False))
                chainToTaskRun = chainEl.GetBoolean();

            return (originalTask, chainToTaskRun);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "CrewMaterialize: could not parse ConfigJson for run {RunId}; defaulting chain_to_task_run=true",
                runId);
            return (null, true);
        }
    }

    private static FinalizerExecutionResult Ok(string actorName) =>
        new(UpdatedText: null, Artifact: null, CostEur: null, ActorName: actorName);

    private static FinalizerExecutionResult Error(Guid runId, string actorName, string message) =>
        new(
            UpdatedText: null,
            Artifact: new RunArtifact
            {
                Id                   = Guid.NewGuid(),
                RunId                = runId,
                FinalizerProfileName = actorName,
                ArtifactType         = ArtifactType.Status,
                StorageUri           = "error",
                StatusMessage        = message,
                CreatedAt            = DateTimeOffset.UtcNow,
            },
            CostEur:   null,
            ActorName: actorName);
}
