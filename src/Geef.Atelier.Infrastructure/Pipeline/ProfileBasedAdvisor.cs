using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Persistence.Crew;
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
/// </summary>
internal sealed class ProfileBasedAdvisor(
    AdvisorProfile profile,
    ILlmClientResolver resolver,
    IAdvisorConsultationRepository consultations,
    Guid runId,
    IPricingCatalog? pricingCatalog = null,
    ICostAccumulator? costAccumulator = null) : IAdvisor
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

        if (costAccumulator is not null)
        {
            var costEur = pricingCatalog?.CalculateCostEur(
                model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, profile.Provider);
            costAccumulator.RecordActorCost(
                iteration, ActorType.Advisor, profile.Name, model,
                response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, costEur,
                providerName: profile.Provider);
        }

        await consultations.CreateAsync(new AdvisorConsultation(
            Id:                 Guid.NewGuid(),
            RunId:              runId,
            IterationNumber:    iteration,
            AdvisorProfileName: profile.Name,
            Output:             response.Text,
            CreatedAt:          DateTimeOffset.UtcNow), cancellationToken);

        return new SdkAdvisorResponse
        {
            AdviceText            = response.Text,
            Confidence            = AdvisorConfidence.Medium,
            Outcome               = AdvisorOutcome.Success,
            ApproximateTokenCount = response.TokenUsage.InputTokens + response.TokenUsage.OutputTokens
        };
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
