using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Research;

#pragma warning disable CS9113

/// <summary>
/// One structured call on the model of <see cref="ModelSlot.GroundingRefiner"/> with the schema <see cref="DossierSchema.Name"/>,
/// <c>reasoning_effort</c> <see cref="ReasoningEffort"/>, without failover and with its own hard time limit
/// (<see cref="ResearchOptions.CondenseTimeout"/>, gateway limit <see cref="ProxyTimeoutSeconds"/>). No second attempt, no other model.
/// </summary>
internal sealed class LlmDossierCondenser(
    IModelAssignmentService models,
    ILlmClientResolver resolver,
    IOptions<ResearchOptions> options,
    ILogger<LlmDossierCondenser> logger,
    TimeProvider time) : IDossierCondenser
{
    public const string ReasoningEffort = "low";
    public const int ProxyTimeoutSeconds = 55;

    public Task<CondenseOutcome> CondenseAsync(IReadOnlyList<string> topics, IReadOnlyList<ResearchPage> pages, CancellationToken ct) =>
        throw new NotImplementedException();
}
