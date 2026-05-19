using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Finalizers;

internal sealed class TransformFinalizerExecutor(
    ILlmClientResolver llmClientResolver,
    IPricingCatalog pricingCatalog,
    ILogger<TransformFinalizerExecutor> logger) : IFinalizerExecutor
{
    public FinalizerType Type => FinalizerType.Transform;

    public async Task<FinalizerExecutionResult> ExecuteAsync(
        FinalizerProfile profile,
        FinalizerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var settings = TransformSettings.From(profile.Settings);

        if (string.IsNullOrWhiteSpace(settings.SystemPrompt))
        {
            logger.LogWarning("Transform finalizer {Profile} has no system prompt configured; skipping.",
                profile.Name);
            return new FinalizerExecutionResult(null, null, null, profile.Name);
        }

        string? updatedText = null;
        RunArtifact? errorArtifact = null;
        decimal? costEur = null;
        int inputTokens = 0, outputTokens = 0;

        try
        {
            var (client, model, maxTokens) = llmClientResolver.ForProfile(
                settings.Provider, settings.Model, settings.MaxTokens);

            var response = await client.CompleteAsync(new LlmRequest
            {
                Model = model,
                SystemPrompt = settings.SystemPrompt,
                UserPrompt = context.CurrentText,
                MaxTokens = maxTokens,
            }, cancellationToken);

            updatedText = response.Text;
            inputTokens = response.TokenUsage.InputTokens;
            outputTokens = response.TokenUsage.OutputTokens;
            costEur = pricingCatalog.CalculateCostEur(model, inputTokens, outputTokens);

            logger.LogInformation(
                "Transform finalizer {Profile} completed: {In}→{Out} tokens, model={Model}",
                profile.Name, inputTokens, outputTokens, model);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transform finalizer failed for profile {Profile}", profile.Name);
            errorArtifact = new RunArtifact
            {
                Id = Guid.NewGuid(),
                RunId = context.RunId,
                FinalizerProfileName = profile.Name,
                ArtifactType = ArtifactType.Status,
                StorageUri = "error",
                StatusMessage = $"Transform failed: {ex.Message}",
                CreatedAt = DateTimeOffset.UtcNow,
            };
        }

        return new FinalizerExecutionResult(
            UpdatedText: updatedText,
            Artifact: errorArtifact,
            CostEur: costEur,
            ActorName: profile.Name,
            ModelName: settings.Model,
            InputTokens: inputTokens,
            OutputTokens: outputTokens);
    }
}
