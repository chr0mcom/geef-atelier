using System.Text.Json;
using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Finalizers;

internal sealed class TransformFinalizerExecutor(
    ILlmClientResolver llmClientResolver,
    IPricingCatalog pricingCatalog,
    IServiceScopeFactory scopeFactory,
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

        // Validate that the configured provider exists and is active.
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var providerService = scope.ServiceProvider.GetRequiredService<IProviderService>();
            var provider = await providerService.GetByNameAsync(settings.Provider, cancellationToken);
            if (provider is null || !provider.IsActive)
            {
                logger.LogWarning(
                    "Transform finalizer {Profile}: provider '{Provider}' not found or inactive; skipping.",
                    profile.Name, settings.Provider);
                return new FinalizerExecutionResult(
                    UpdatedText: null,
                    Artifact: new RunArtifact
                    {
                        Id = Guid.NewGuid(),
                        RunId = context.RunId,
                        FinalizerProfileName = profile.Name,
                        ArtifactType = ArtifactType.Status,
                        StorageUri = "error",
                        StatusMessage = $"Finalizer provider '{settings.Provider}' not found or inactive",
                        CreatedAt = DateTimeOffset.UtcNow,
                    },
                    CostEur: null,
                    ActorName: profile.Name);
            }
        }

        string? updatedText = null;
        RunArtifact? artifact = null;
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

            inputTokens = response.TokenUsage.InputTokens;
            outputTokens = response.TokenUsage.OutputTokens;
            costEur = pricingCatalog.CalculateCostEur(model, inputTokens, outputTokens, settings.Provider);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                logger.LogWarning(
                    "Transform finalizer {Profile} returned empty response; original text preserved.", profile.Name);
                artifact = MakeStatusArtifact(context.RunId, profile.Name, "warning",
                    "Transform returned empty response — original text preserved.");
            }
            else
            {
                updatedText = response.Text;
                var chunks = ParagraphDiff.Compute(context.CurrentText, updatedText);
                var noChanges = chunks.All(c => c.Op == ParagraphDiff.Op.Equal);
                var diffJson = BuildDiffJson(settings.Model, settings.Provider, noChanges, chunks);
                artifact = MakeStatusArtifact(context.RunId, profile.Name,
                    noChanges ? "no-changes" : "transform-diff", diffJson);

                logger.LogInformation(
                    "Transform finalizer {Profile} completed: {In}→{Out} tokens, model={Model}, changed={Changed}",
                    profile.Name, inputTokens, outputTokens, model, !noChanges);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transform finalizer failed for profile {Profile}", profile.Name);
            artifact = MakeStatusArtifact(context.RunId, profile.Name, "error",
                $"Transform failed: {ex.Message}");
        }

        return new FinalizerExecutionResult(
            UpdatedText: updatedText,
            Artifact: artifact,
            CostEur: costEur,
            ActorName: profile.Name,
            ModelName: settings.Model,
            InputTokens: inputTokens,
            OutputTokens: outputTokens,
            ProviderName: settings.Provider);
    }

    private static RunArtifact MakeStatusArtifact(Guid runId, string profileName, string storageUri, string? message) =>
        new()
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            FinalizerProfileName = profileName,
            ArtifactType = ArtifactType.Status,
            StorageUri = storageUri,
            StatusMessage = message,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static string BuildDiffJson(string model, string provider, bool noChanges, IReadOnlyList<ParagraphDiff.Chunk> chunks)
    {
        var obj = new
        {
            model,
            provider,
            noChanges,
            chunks = chunks.Select(c => new
            {
                op = c.Op.ToString().ToLowerInvariant(),
                text = c.Text
            }).ToArray()
        };
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }
}
