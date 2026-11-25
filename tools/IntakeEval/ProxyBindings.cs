using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tools.IntakeEval;

/// <summary>Always assigns the given model; the tool has no live catalog and no database.</summary>
internal sealed class FixedModelAssignment(ModelCandidate candidate) : IModelAssignmentService
{
    public Task<ModelAssignment> AssignAsync(ModelSlot slot, ModelDomain domain, ModelCandidate? executor = null, CancellationToken ct = default) =>
        Task.FromResult(new ModelAssignment(candidate, false, null));

    public Task<IReadOnlyList<ModelAssignment>> AssignReviewersAsync(IReadOnlyList<ReviewerAssignmentRequest> reviewers, ModelDomain crewDomain, ModelCandidate executor, CancellationToken ct = default) =>
        throw new NotSupportedException("The intake measurement assigns only the intake slot.");

    public Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, ICollection<ModelAssignmentWarning> warnings, CancellationToken ct = default) =>
        throw new NotSupportedException("The intake measurement assigns only the intake slot.");
}

/// <summary>Sends every call to one OpenAI-compatible endpoint (the gateway route of the CLI), with structured outputs.</summary>
internal sealed class ProxyResolver(ILlmClient client) : ILlmClientResolver
{
    public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) =>
        throw new NotSupportedException("The intake measurement uses profiles only.");

    public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens) =>
        (client, model, maxTokens ?? new IntakeOptions().MaxTokens);

    public bool SupportsAgenticTools(string providerName) => false;

    public bool SupportsStructuredOutputs(string providerName) => true;
}
