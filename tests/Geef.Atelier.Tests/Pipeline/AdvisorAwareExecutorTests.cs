using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Verifies the advisor-aware executor decorator via full pipeline runs using snapshot-based builds.
/// Advisor invocations are tracked through the in-memory <see cref="IAdvisorConsultationRepository"/>.
/// </summary>
public sealed class AdvisorAwareExecutorTests
{
    private const string Briefing = "Schreib einen kurzen Text über Advisor-Pässe.";

    private static AdvisorProfile MakeAdvisor(string name, AdvisorTrigger trigger) => new(
        Name: name,
        DisplayName: name,
        Description: "Test advisor",
        SystemPrompt: "You are a test advisor.",
        Provider: "openrouter",
        Model: "google/gemini-2.5-flash",
        MaxTokens: null,
        Mode: AdvisorMode.Strategic,
        Trigger: trigger,
        IsSystem: false);

    private static CrewSnapshot BuildSnapshot(IReadOnlyList<AdvisorProfile> advisors) => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.KlassikTemplateName,
        Executor: SystemCrew.DefaultExecutorProfile,
        Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        Advisors: advisors);

    [Fact]
    public async Task AdvisorAwareExecutor_WithNoAdvisors_CallsInnerExecutorDirectly()
    {
        // With no advisors, the consultation repository should remain empty.
        var resolver    = new TestLlmClientResolver(new FakeLlmClient());
        var snapshot    = BuildSnapshot(Array.Empty<AdvisorProfile>());
        var consultRepo = new InMemoryAdvisorConsultationRepository();
        var runId       = Guid.NewGuid();

        var runner = AtelierPipelineFactory.Build(
            snapshot, resolver, Options.Create(new ConvergenceOptions()),
            consultationRepository: consultRepo,
            runId: runId);

        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.True(result.Success);
        var consultations = await consultRepo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Empty(consultations);
    }

    [Fact]
    public async Task AdvisorAwareExecutor_CallsBeforeFirstExecution_OnlyOnIteration1()
    {
        // BeforeFirstExecution advisor should be consulted exactly once — before iteration 1 only.
        // Uses routing client to isolate advisor LLM calls from executor/reviewer LLM calls so
        // the FakeLlmClient's internal counter is not skewed by advisor plain-text calls.
        var fakeExecutorReviewer = new FakeLlmClient();
        var resolver    = new TestLlmClientResolver(
            new SystemPromptRoutingClient("You are a test advisor.", new AlwaysTextClient("advisor output"), fakeExecutorReviewer));
        var advisor     = MakeAdvisor("test-clarifier", AdvisorTrigger.BeforeFirstExecution);
        var snapshot    = BuildSnapshot([advisor]);
        var consultRepo = new InMemoryAdvisorConsultationRepository();
        var runId       = Guid.NewGuid();

        var runner = AtelierPipelineFactory.Build(
            snapshot, resolver, Options.Create(new ConvergenceOptions()),
            consultationRepository: consultRepo,
            runId: runId);

        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.True(result.Success);
        var consultations = await consultRepo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Single(consultations);
        Assert.Equal(1, consultations[0].IterationNumber); // consulted before iteration 1
    }

    [Fact]
    public async Task AdvisorAwareExecutor_CallsBeforeEveryExecution_OnEveryIteration()
    {
        // BeforeEveryExecution advisor should be consulted before each executor iteration.
        // Uses routing client to isolate advisor calls so FakeLlmClient converges in 2 iterations.
        var fakeExecutorReviewer = new FakeLlmClient();
        var resolver    = new TestLlmClientResolver(
            new SystemPromptRoutingClient("You are a test advisor.", new AlwaysTextClient("advisor output"), fakeExecutorReviewer));
        var advisor     = MakeAdvisor("test-devils-advocate", AdvisorTrigger.BeforeEveryExecution);
        var snapshot    = BuildSnapshot([advisor]);
        var consultRepo = new InMemoryAdvisorConsultationRepository();
        var runId       = Guid.NewGuid();

        var runner = AtelierPipelineFactory.Build(
            snapshot, resolver, Options.Create(new ConvergenceOptions()),
            consultationRepository: consultRepo,
            runId: runId);

        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.True(result.Success);
        // FakeLlmClient rejects on iteration 1, approves on iteration 2 → 2 advisor consultations
        var consultations = await consultRepo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Equal(2, consultations.Count);
        Assert.All(consultations, c => Assert.Equal("test-devils-advocate", c.AdvisorProfileName));
    }

    [Fact]
    public async Task AdvisorAwareExecutor_SkipsOnConvergenceFailure_Trigger()
    {
        // OnConvergenceFailure advisors are excluded from the pre-execution decorator.
        // During a normal run (no convergence failure) the repository should remain empty.
        var resolver    = new TestLlmClientResolver(new FakeLlmClient());
        var advisor     = MakeAdvisor("test-recovery-advisor", AdvisorTrigger.OnConvergenceFailure);
        var snapshot    = BuildSnapshot([advisor]);
        var consultRepo = new InMemoryAdvisorConsultationRepository();
        var runId       = Guid.NewGuid();

        var runner = AtelierPipelineFactory.Build(
            snapshot, resolver, Options.Create(new ConvergenceOptions()),
            consultationRepository: consultRepo,
            runId: runId);

        var result = await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.True(result.Success);
        var consultations = await consultRepo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Empty(consultations);
    }

    [Fact]
    public async Task AdvisorAwareExecutor_InjectsAdvisorBlock_IntoContext()
    {
        // When an advisor runs, its consultation record should be persisted with the correct metadata.
        var resolver    = new TestLlmClientResolver(
            new SystemPromptRoutingClient("You are a test advisor.", new AlwaysTextClient("advisor output"), new FakeLlmClient()));
        var advisor     = MakeAdvisor("test-advisor", AdvisorTrigger.BeforeFirstExecution);
        var snapshot    = BuildSnapshot([advisor]);
        var consultRepo = new InMemoryAdvisorConsultationRepository();
        var runId       = Guid.NewGuid();

        var runner = AtelierPipelineFactory.Build(
            snapshot, resolver, Options.Create(new ConvergenceOptions()),
            consultationRepository: consultRepo,
            runId: runId);

        await runner.RunAsync(Briefing, CancellationToken.None);

        var consultations = await consultRepo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Single(consultations);
        Assert.Equal("test-advisor", consultations[0].AdvisorProfileName);
        Assert.Equal(runId, consultations[0].RunId);
        Assert.False(string.IsNullOrEmpty(consultations[0].Output));
    }

    // --- Helper LLM clients ---

    /// <summary>
    /// Routes LLM calls to different clients based on the system prompt, allowing
    /// advisor calls to be isolated from executor/reviewer calls.
    /// </summary>
    private sealed class SystemPromptRoutingClient(
        string advisorSystemPrompt,
        ILlmClient advisorClient,
        ILlmClient defaultClient) : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
            request.SystemPrompt == advisorSystemPrompt
                ? advisorClient.CompleteAsync(request, ct)
                : defaultClient.CompleteAsync(request, ct);
    }

    /// <summary>Always returns a fixed text response — used for advisor calls to avoid skewing iteration counters.</summary>
    private sealed class AlwaysTextClient(string text) : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
            Task.FromResult(FakeLlmClient.MakeTextResponse(text));
    }

    // --- In-memory repository ---

    private sealed class InMemoryAdvisorConsultationRepository : IAdvisorConsultationRepository
    {
        private readonly List<AdvisorConsultation> _store = [];

        public Task<AdvisorConsultation> CreateAsync(AdvisorConsultation consultation, CancellationToken ct)
        {
            _store.Add(consultation);
            return Task.FromResult(consultation);
        }

        public Task<IReadOnlyList<AdvisorConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
        {
            IReadOnlyList<AdvisorConsultation> result = _store
                .Where(c => c.RunId == runId)
                .OrderBy(c => c.CreatedAt)
                .ToList();
            return Task.FromResult(result);
        }
    }
}
