using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Advisors;
using Geef.Sdk.Context;
using SdkAdvisorKind = Geef.Sdk.Advisors.AdvisorKind;
using SdkAdvisorQuery = Geef.Sdk.Advisors.AdvisorQuery;
using SdkAdvisorResponse = Geef.Sdk.Advisors.AdvisorResponse;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// SDK <see cref="IAdvisor"/> backed by an <see cref="AdvisorProfile"/>. Calls the LLM
/// with the profile's system prompt and the run context's grounded brief, persists the
/// result as an <see cref="AdvisorConsultation"/>, and returns an <see cref="SdkAdvisorResponse"/>.
/// When <see cref="AdvisorProfile.ToolNames"/> is non-empty and the provider supports agentic
/// tool use, the advisor runs a multi-turn tool-use loop before returning its final advice text.
/// </summary>
internal sealed class ProfileBasedAdvisor(
    AdvisorProfile profile,
    ILlmClientResolver resolver,
    IAdvisorConsultationRepository consultations,
    Guid runId,
    IPricingCatalog? pricingCatalog = null,
    ICostAccumulator? costAccumulator = null,
    IToolUseRunner? toolUseRunner = null,
    IToolDefinitionRepository? toolDefinitionRepository = null) : IAdvisor
{
    public string Name => profile.Name;
    public SdkAdvisorKind Kind => MapKind(profile.Mode);

    public async Task<SdkAdvisorResponse> ConsultAsync(
        SdkAdvisorQuery query,
        IRunContext context,
        CancellationToken cancellationToken = default)
    {
        var briefing  = context.TryGet(AtelierContextKeys.GroundedBrief, out var b) ? b ?? "" : "";
        var iteration = context.TryGet(GeefKeys.CurrentIteration, out var i) ? i : 0;

        var (client, model, maxTokens) = resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);

        string adviceText;
        int inputTokens;
        int outputTokens;

        // Agentic tool-use loop path: when the profile has bound tools and the provider supports it.
        if (profile.ToolNames is { Count: > 0 } toolNames
            && toolUseRunner is not null
            && toolDefinitionRepository is not null
            && resolver.SupportsAgenticTools(profile.Provider))
        {
            var boundTools = await ResolveToolsAsync(toolNames, toolDefinitionRepository, cancellationToken);

            var loopCtx = new ToolInvocationContext(
                RunId: runId,
                IterationNumber: iteration,
                ActorType: "advisor",
                ActorName: profile.Name,
                Sequence: 0);

            var loopResult = await toolUseRunner.RunAsync(
                client, model,
                profile.SystemPrompt,
                briefing,
                boundTools,
                requiredFinalTool: null,
                new ToolLoopOptions { MaxToolCalls = toolNames.Count * 3 },
                loopCtx,
                cancellationToken);

            adviceText   = loopResult.FinalText;
            inputTokens  = loopResult.TotalTokenUsage.InputTokens;
            outputTokens = loopResult.TotalTokenUsage.OutputTokens;
        }
        else
        {
            // Standard single-shot path.
            var response = await LlmResilience.ExecuteAsync(
                token => client.CompleteAsync(new LlmRequest
                {
                    Model        = model,
                    SystemPrompt = profile.SystemPrompt,
                    UserPrompt   = briefing,
                    MaxTokens    = maxTokens
                }, token),
                cancellationToken,
                maxAttempts: LlmResilience.ReviewerMaxAttempts,
                maxDelay: LlmResilience.ReviewerMaxDelay);

            adviceText   = response.Text;
            inputTokens  = response.TokenUsage.InputTokens;
            outputTokens = response.TokenUsage.OutputTokens;

            if (costAccumulator is not null)
            {
                var costEur = pricingCatalog?.CalculateCostEur(
                    model, inputTokens, outputTokens, profile.Provider,
                    cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0);
                costAccumulator.RecordActorCost(
                    iteration, ActorType.Advisor, profile.Name, model,
                    inputTokens, outputTokens, costEur,
                    providerName: profile.Provider,
                    cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0,
                    reasoningTokens: response.TokenUsage.ReasoningTokens ?? 0);
            }
        }

        await consultations.CreateAsync(new AdvisorConsultation(
            Id:                 Guid.NewGuid(),
            RunId:              runId,
            IterationNumber:    iteration,
            AdvisorProfileName: profile.Name,
            Output:             adviceText,
            CreatedAt:          DateTimeOffset.UtcNow), cancellationToken);

        return new SdkAdvisorResponse
        {
            AdviceText            = adviceText,
            Confidence            = AdvisorConfidence.Medium,
            Outcome               = AdvisorOutcome.Success,
            ApproximateTokenCount = inputTokens + outputTokens
        };
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<IReadOnlyList<ToolDefinition>> ResolveToolsAsync(
        IReadOnlyList<string> toolNames,
        IToolDefinitionRepository repository,
        CancellationToken ct)
    {
        var tools = new List<ToolDefinition>(toolNames.Count);
        foreach (var name in toolNames)
        {
            var tool = await repository.GetByNameAsync(name, ct);
            if (tool is not null)
                tools.Add(tool);
        }
        return tools;
    }

    private static SdkAdvisorKind MapKind(AdvisorMode mode) => mode switch
    {
        AdvisorMode.Strategic      => SdkAdvisorKind.Strategic,
        AdvisorMode.Critical       => SdkAdvisorKind.Critical,
        AdvisorMode.DevilsAdvocate => SdkAdvisorKind.Socratic,
        AdvisorMode.DomainExpert   => SdkAdvisorKind.Calibration,
        _                          => SdkAdvisorKind.Strategic
    };
}
