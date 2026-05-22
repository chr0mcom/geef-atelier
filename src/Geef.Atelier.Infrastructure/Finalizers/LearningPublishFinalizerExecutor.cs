using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Finalizers;

/// <summary>
/// Runs inside a learning-evaluation run. If the run converged, computes an embedding
/// and marks the learning candidate Approved. If the run failed/aborted, marks it Rejected.
/// Guards against running in Standard runs (Recursion Stop #2).
/// </summary>
internal sealed class LearningPublishFinalizerExecutor(
    IServiceScopeFactory scopeFactory,
    ILogger<LearningPublishFinalizerExecutor> logger) : IFinalizerExecutor
{
    public FinalizerType Type => FinalizerType.LearningPublish;

    public async Task<FinalizerExecutionResult> ExecuteAsync(
        FinalizerProfile profile,
        FinalizerExecutionContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var runRepo      = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            var learningRepo = scope.ServiceProvider.GetRequiredService<ILearningRepository>();

            // ── Recursion guard (Recursion Stop #2) ──────────────────────────
            var run = await runRepo.GetByIdAsync(context.RunId, cancellationToken);
            if (run is null)
            {
                logger.LogWarning("LearningPublish: run {RunId} not found; skipping.", context.RunId);
                return Ok(profile.Name);
            }

            if (run.Kind != RunKind.Learning)
            {
                logger.LogDebug("LearningPublish: run {RunId} is not a Learning run; skipping to prevent misfire.", context.RunId);
                return Ok(profile.Name);
            }

            // ── Find the candidate entry via LearningRunId ────────────────────
            // The extractor set LearningRunId on the entry after submit.
            var allProposed = await learningRepo.ListAsync(
                status: LearningStatus.Proposed, ct: cancellationToken);
            var entry = allProposed.FirstOrDefault(e => e.LearningRunId == context.RunId);

            if (entry is null)
            {
                logger.LogWarning(
                    "LearningPublish: no Proposed LearningEntry found with LearningRunId={RunId}; skipping.",
                    context.RunId);
                return Ok(profile.Name);
            }

            // ── Run converged → Approve with embedding ────────────────────────
            if (run.Status == RunStatus.Completed && !string.IsNullOrWhiteSpace(context.FinalText))
            {
                float[] embedding = [];
                try
                {
                    var embeddingProvider = scope.ServiceProvider.GetRequiredService<IEmbeddingProvider>();
                    var result = await embeddingProvider.CreateAsync(context.FinalText, cancellationToken);
                    embedding = result.Vector;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "LearningPublish: embedding failed for entry {EntryId}; approving without embedding.",
                        entry.Id);
                }

                await learningRepo.SetEmbeddingAsync(entry.Id, embedding, cancellationToken);
                await learningRepo.UpdateStatusAsync(
                    entry.Id, LearningStatus.Approved, DateTimeOffset.UtcNow, cancellationToken);

                logger.LogInformation(
                    "LearningPublish: entry {EntryId} Approved (learning run {RunId}).",
                    entry.Id, context.RunId);
            }
            else
            {
                // Run did not converge → Reject
                await learningRepo.UpdateStatusAsync(
                    entry.Id, LearningStatus.Rejected, null, cancellationToken);

                logger.LogInformation(
                    "LearningPublish: entry {EntryId} Rejected (run status={Status}, learning run {RunId}).",
                    entry.Id, run.Status, context.RunId);
            }

            return Ok(profile.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LearningPublish: unexpected error for run {RunId}", context.RunId);
            return Error(context.RunId, profile.Name, $"Unexpected error: {ex.Message}");
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
