using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Geef.Sdk;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Microsoft.Extensions.DependencyInjection;
using SdkSeverity = Geef.Sdk.Results.FindingSeverity;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// A plain write-and-review loop for tests of the orchestrator, the event sink and the live pages, which test what happens around a
/// pipeline and not the direct path itself. It runs the snapshots of <see cref="TestLoop.TemplateName"/>: the writer asks the LLM client
/// for the text (a request without tools), each reviewer asks it for a review with the tool <c>submit_review</c>. Fakes such as
/// <c>FakeLlmClient</c> answer both.
/// </summary>
internal static class TestLoop
{
    /// <summary>Template name of the snapshots this loop runs.</summary>
    public const string TemplateName = "test-loop";

    /// <summary>The writer of the test loop.</summary>
    public static readonly ExecutorProfile Writer = new(
        Name: "test-writer", DisplayName: "Writer", Description: "Writes the test text.", SystemPrompt: "Write the text.",
        Provider: "openrouter", Model: "test-model", MaxTokens: 4000, IsSystem: true);

    /// <summary>The two reviewers of the test loop.</summary>
    public static readonly IReadOnlyList<ReviewerProfile> Reviewers =
    [
        new("test-fidelity", "Fidelity", "Checks the request.", "Review the text.", "openrouter", "test-model", 4000, IsSystem: true),
        new("test-clarity", "Clarity", "Checks the clarity.", "Review the text.", "openrouter", "test-model", 4000, IsSystem: true),
    ];

    /// <summary>The snapshot of a test-loop run, with at most <paramref name="maxIterations"/> rounds.</summary>
    public static CrewSnapshot Snapshot(int maxIterations = 3) => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: TemplateName,
        Executor: Writer,
        Reviewers: Reviewers,
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: new ConvergencePolicyOverride(maxIterations, AbortOnCritical: true, DetectRegression: false, StagnationThreshold: maxIterations),
        Advisors: []);

    /// <summary>The pipeline of <paramref name="snapshot"/> with the writer and reviewers of the test loop, answered by <paramref name="resolver"/>.</summary>
    public static GeefPipelineRunner<FinalizedDocument> Build(CrewSnapshot snapshot, ILlmClientResolver resolver,
        Microsoft.Extensions.Options.IOptions<Geef.Atelier.Infrastructure.Configuration.ConvergenceOptions> convergence,
        IEnumerable<Geef.Sdk.Events.IGeefEventSink>? sinks = null, Func<ReviewerProfile, IReviewer?>? reviewers = null) =>
        AtelierPipelineFactory.Build(snapshot, convergence, new TestLoopGrounding(), new TestLoopWriter(snapshot.Executor, resolver),
            reviewers ?? (profile => new TestLoopReviewer(profile, resolver)), null, sinks);

    /// <summary>Registers <see cref="TestLoopAssembler"/> beside the assemblers of the application.</summary>
    public static IServiceCollection AddTestLoopAssembler(this IServiceCollection services) =>
        services.AddSingleton<IPipelineAssembler, TestLoopAssembler>();
}

/// <summary>Builds the pipeline of a run of <see cref="TestLoop.TemplateName"/>.</summary>
internal sealed class TestLoopAssembler : IPipelineAssembler
{
    public int Priority => -1;

    public bool CanAssemble(RunEntity run, CrewSnapshot snapshot) => snapshot.TemplateName == TestLoop.TemplateName;

    public GeefPipelineRunner<FinalizedDocument> Assemble(PipelineAssemblyContext context) =>
        AtelierPipelineFactory.Build(
            context.Snapshot, context.ConvergenceOptions, new TestLoopGrounding(),
            new TestLoopWriter(context.Snapshot.Executor, context.LlmClientResolver, context.CostAccumulator),
            profile => new TestLoopReviewer(profile, context.LlmClientResolver, context.CostAccumulator),
            context.LoggerFactory,
            [context.EventSink]);
}

/// <summary>Hands the request to the writer as the brief.</summary>
internal sealed class TestLoopGrounding : IGroundingStep
{
    public Task<GroundingResult> RunAsync(string input, CancellationToken cancellationToken) =>
        Task.FromResult(new GroundingResult { Context = new RunContext().Set(AtelierContextKeys.GroundedBrief, input), Notes = [] });
}

/// <summary>Asks the LLM client for the text of each round.</summary>
internal sealed class TestLoopWriter(ExecutorProfile profile, ILlmClientResolver resolver, ICostAccumulator? costs = null) : IExecutionStep
{
    public async Task<ExecutionResult> RunAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var iteration = context.GetRequired(GeefKeys.CurrentIteration);
        var (client, model, maxTokens) = resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);
        var response = await client.CompleteAsync(new LlmRequest
        {
            Model = model,
            SystemPrompt = profile.SystemPrompt,
            UserPrompt = context.GetRequired(AtelierContextKeys.GroundedBrief),
            MaxTokens = maxTokens,
        }, cancellationToken);
        costs?.RecordActorCost(iteration, ActorType.Executor, profile.Name, model,
            response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, null, providerName: profile.Provider);
        return new ExecutionResult
        {
            UpdatedContext = context.Set(AtelierContextKeys.CurrentDraft, response.Text).Set(AtelierContextKeys.TokenUsage, response.TokenUsage),
            Notes = [],
        };
    }
}

/// <summary>Asks the LLM client for a review with the tool <c>submit_review</c>; critical and major findings block.</summary>
internal sealed class TestLoopReviewer(ReviewerProfile profile, ILlmClientResolver resolver, ICostAccumulator? costs = null) : IReviewer
{
    private static readonly LlmTool SubmitReview = new()
    {
        Name = "submit_review",
        Description = "Submit the review.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{"approved":{"type":"boolean"},"findings":{"type":"array"}},"required":["approved","findings"]}"""),
    };

    public string Name => profile.Name;

    public int Priority => 0;

    public async Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var iteration = context.GetRequired(GeefKeys.CurrentIteration);
        var (client, model, maxTokens) = resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);
        var response = await client.CompleteAsync(new LlmRequest
        {
            Model = model,
            SystemPrompt = profile.SystemPrompt,
            UserPrompt = context.GetRequired(AtelierContextKeys.CurrentDraft),
            MaxTokens = maxTokens,
            Tools = [SubmitReview],
            ToolChoice = "function:submit_review",
        }, cancellationToken);
        costs?.RecordActorCost(iteration, ActorType.Reviewer, profile.Name, model,
            response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, null, providerName: profile.Provider);

        if (response.FinishReason != "tool_calls" || response.ToolArgumentsJson is null)
            return new ReviewResult { ReviewerName = profile.Name, Decision = ReviewDecision.Failed, Findings = [], Duration = TimeSpan.Zero };

        using var document = JsonDocument.Parse(response.ToolArgumentsJson);
        var findings = document.RootElement.GetProperty("findings").EnumerateArray()
            .Select(f => (Severity: f.GetProperty("severity").GetString() ?? "minor", Message: f.GetProperty("message").GetString() ?? string.Empty))
            .Where(f => f.Message.Length > 0)
            .Select(f => new Finding
            {
                ReviewerName = profile.Name,
                Fingerprint = $"{profile.Name}:{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(f.Message)))[..12]}",
                Message = f.Message,
                Severity = f.Severity switch
                {
                    "critical" => SdkSeverity.Critical,
                    "major" => SdkSeverity.Error,
                    "info" => SdkSeverity.Info,
                    _ => SdkSeverity.Warning,
                },
                Category = "review",
                ArtifactReference = "draft",
                Metadata = new Dictionary<string, object>(),
            })
            .ToList();

        return new ReviewResult
        {
            ReviewerName = profile.Name,
            Decision = findings.Count == 0 ? ReviewDecision.Approved : ReviewDecision.ApprovedWithWarnings,
            Findings = findings,
            Duration = TimeSpan.Zero,
        };
    }
}
