using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Finalizers;

/// <summary>
/// Extracts a structured learning candidate from a completed run, then fires a
/// learning-evaluation run as fire-and-forget. Runs only for Standard runs;
/// returns immediately for Learning runs (recursion guard).
/// </summary>
internal sealed class LearningExtractFinalizerExecutor(
    ILlmClientResolver llmClientResolver,
    IServiceScopeFactory scopeFactory,
    ILogger<LearningExtractFinalizerExecutor> logger) : IFinalizerExecutor
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public FinalizerType Type => FinalizerType.LearningExtract;

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
            var runService   = scope.ServiceProvider.GetRequiredService<IRunService>();

            // ── Recursion guard (Recursion Stop #1) ──────────────────────────
            var run = await runRepo.GetByIdAsync(context.RunId, cancellationToken);
            if (run is null)
            {
                logger.LogWarning("LearningExtract: run {RunId} not found; skipping.", context.RunId);
                return Ok(profile.Name);
            }

            if (run.Kind == RunKind.Learning)
            {
                logger.LogDebug("LearningExtract: run {RunId} is a Learning run; skipping to prevent recursion.", context.RunId);
                return Ok(profile.Name);
            }

            // ── Interestingness threshold ─────────────────────────────────────
            var settings = LearningExtractSettings.From(profile.Settings);
            var details  = await runRepo.GetDetailsAsync(context.RunId, cancellationToken);
            if (details is null)
            {
                logger.LogWarning("LearningExtract: details for run {RunId} not found; skipping.", context.RunId);
                return Ok(profile.Name);
            }

            var allFindings    = details.Iterations.SelectMany(i => i.Findings).ToList();
            var hasSignificant = allFindings.Any(f => f.Severity <= FindingSeverity.Major);
            var iterCount      = details.Iterations.Count;

            if (iterCount < settings.MinIterations && !(hasSignificant && !settings.RequireMajorFinding))
            {
                logger.LogInformation(
                    "LearningExtract: run {RunId} did not meet threshold (iters={Iter}, significant={Sig}); skipping.",
                    context.RunId, iterCount, hasSignificant);
                return Ok(profile.Name);
            }

            // ── Build structured facts ────────────────────────────────────────
            var factsJson = BuildStructuredFacts(run, details, allFindings);

            // ── LLM extraction call ───────────────────────────────────────────
            string candidateText;
            int inputTokens = 0, outputTokens = 0;
            decimal? costEur = null;

            try
            {
                var (client, model, maxTokens) = llmClientResolver.ForProfile(
                    settings.Provider, settings.Model, settings.MaxTokens);

                const string systemPrompt =
                    """
                    You are a precise learning extractor. Your task is to formulate a single, concise,
                    generalisable insight from an AI writing run.

                    Rules:
                    1. Only use facts explicitly present in the structured run data provided.
                    2. Do NOT invent, hallucinate, or extrapolate beyond the provided evidence.
                    3. The insight must be generalisable: it should help future similar runs, not just describe this run.
                    4. Write in the same language as the briefing text.
                    5. Maximum 3 sentences. Be specific. Avoid vague platitudes.
                    """;

                var response = await client.CompleteAsync(new LlmRequest
                {
                    Model        = model,
                    SystemPrompt = systemPrompt,
                    UserPrompt   = factsJson,
                    MaxTokens    = maxTokens,
                }, cancellationToken);

                candidateText = response.Text;
                inputTokens   = response.TokenUsage.InputTokens;
                outputTokens  = response.TokenUsage.OutputTokens;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LearningExtract: LLM call failed for run {RunId}", context.RunId);
                return Error(context.RunId, profile.Name, $"LLM extraction failed: {ex.Message}");
            }

            // ── Persist proposed learning entry ───────────────────────────────
            var domain = run.CrewTemplateName ?? "unknown";
            var entry  = new LearningEntry(
                Id:                  Guid.NewGuid(),
                Text:                candidateText,
                SourceRunId:         context.RunId,
                LearningRunId:       null,
                Domain:              domain,
                Status:              LearningStatus.Proposed,
                StructuredFactsJson: factsJson,
                OwnerUsername:       run.CreatedByUser ?? "system",
                CreatedAt:           DateTimeOffset.UtcNow,
                ApprovedAt:          null);

            await learningRepo.CreateAsync(entry, [], cancellationToken);

            // ── Fire-and-forget: learning-evaluation run ──────────────────────
            // SubmitRunAsync creates a Pending row synchronously and returns; the
            // orchestrator polls for Pending runs, so no blocking wait is needed.
            try
            {
                var briefing = BuildLearningBriefing(entry, run.BriefingText);
                var learningRunId = await runService.SubmitRunAsync(
                    new SubmitRunRequest(
                        BriefingText:     briefing,
                        ConfigJson:       "{}",
                        CreatedByUser:    run.CreatedByUser,
                        CrewTemplateName: "learning-evaluation",
                        Kind:             RunKind.Learning),
                    cancellationToken);

                await learningRepo.SetLearningRunIdAsync(entry.Id, learningRunId, cancellationToken);

                logger.LogInformation(
                    "LearningExtract: created LearningEntry {EntryId} and fired learning run {LearningRunId} for source run {SourceRunId}.",
                    entry.Id, learningRunId, context.RunId);
            }
            catch (Exception ex)
            {
                // A submit failure must not fail the original run — the entry remains Proposed
                // and can be retried or manually reviewed.
                logger.LogError(ex,
                    "LearningExtract: failed to submit learning-evaluation run for entry {EntryId}", entry.Id);
            }

            return new FinalizerExecutionResult(
                UpdatedText:  null,
                Artifact:     null,
                CostEur:      costEur,
                ActorName:    profile.Name,
                ModelName:    settings.Model,
                InputTokens:  inputTokens,
                OutputTokens: outputTokens,
                ProviderName: settings.Provider);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LearningExtract: unexpected error for run {RunId}", context.RunId);
            return Error(context.RunId, profile.Name, $"Unexpected error: {ex.Message}");
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string BuildStructuredFacts(
        RunEntity run,
        RunDetails details,
        List<FindingEntity> allFindings)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"domain: {run.CrewTemplateName ?? "unknown"}");
        sb.AppendLine($"iterations: {details.Iterations.Count}");
        sb.AppendLine($"briefing_summary: {Truncate(run.BriefingText, 400)}");
        sb.AppendLine($"final_text_preview: {Truncate(details.Run.FinalText ?? "", 400)}");
        sb.AppendLine("findings:");
        foreach (var f in allFindings.OrderBy(f => f.Severity))
            sb.AppendLine($"  - [{f.Severity}] {f.ReviewerName}: {f.Message}");
        return sb.ToString();
    }

    private static string BuildLearningBriefing(LearningEntry entry, string originalBriefing)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LEARNING EVALUATION REQUEST");
        sb.AppendLine($"Source run domain: {entry.Domain}");
        sb.AppendLine($"Original briefing summary: {Truncate(originalBriefing, 300)}");
        sb.AppendLine();
        sb.AppendLine("CANDIDATE LEARNING:");
        sb.AppendLine(entry.Text);
        sb.AppendLine();
        sb.AppendLine("STRUCTURED RUN FACTS (ground truth for reviewers):");
        sb.AppendLine(entry.StructuredFactsJson);
        sb.AppendLine();
        sb.AppendLine($"learning_entry_id: {entry.Id}");
        sb.AppendLine($"source_run_id: {entry.SourceRunId}");
        return sb.ToString();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

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
