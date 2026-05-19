using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Consults a single <see cref="AdvisorProfile"/> via LLM and persists the resulting
/// <see cref="AdvisorConsultation"/>. Plain-text interaction — no tool use.
/// </summary>
internal sealed class ProfileBasedAdvisor(
    AdvisorProfile profile,
    ILlmClientResolver resolver,
    IAdvisorConsultationRepository consultations,
    IPricingCatalog? pricingCatalog = null,
    ICostAccumulator? costAccumulator = null)
{
    /// <summary>The profile that drives this advisor instance.</summary>
    public AdvisorProfile Profile => profile;

    /// <summary>
    /// Calls the LLM with the advisor's system prompt and the provided briefing text,
    /// persists the response as an <see cref="AdvisorConsultation"/>, and returns it.
    /// </summary>
    /// <param name="runId">The run this consultation belongs to.</param>
    /// <param name="iterationNumber">
    /// The executor iteration this consultation precedes.
    /// Use <c>-1</c> for convergence-failure recovery consultations.
    /// </param>
    /// <param name="briefingText">The current grounded brief forwarded as the user message.</param>
    /// <param name="ct">Cancellation token propagated from the pipeline.</param>
    public async Task<AdvisorConsultation> ConsultAsync(
        Guid runId,
        int iterationNumber,
        string briefingText,
        CancellationToken ct)
    {
        var (client, model, maxTokens) = resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = model,
            SystemPrompt = profile.SystemPrompt,
            UserPrompt   = briefingText,
            MaxTokens    = maxTokens
        }, ct);

        if (costAccumulator is not null && iterationNumber >= 0)
        {
            var costEur = pricingCatalog?.CalculateCostEur(
                model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens);
            costAccumulator.RecordActorCost(
                iterationNumber, ActorType.Advisor, profile.Name, model,
                response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, costEur,
                providerName: profile.Provider);
        }

        var consultation = new AdvisorConsultation(
            Id:                 Guid.NewGuid(),
            RunId:              runId,
            IterationNumber:    iterationNumber,
            AdvisorProfileName: profile.Name,
            Output:             response.Text,
            CreatedAt:          DateTimeOffset.UtcNow);

        return await consultations.CreateAsync(consultation, ct);
    }
}
