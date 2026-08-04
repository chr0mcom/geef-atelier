using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Composition;

/// <summary>
/// Step 10 of the validator: a reviewer that ends up on the executor's model turns the crew into a
/// monoculture reviewing itself. Because an always-latest alias and the concrete id it points at are
/// the same model under two names, both sides must be compared after resolution — comparing the raw
/// strings would let exactly that case through.
/// </summary>
public sealed class CrewSpecValidatorPluralityTests
{
    private const string AliasedExecutor =
        """{"name":"task-executor","provider":"claude-cli","model":"claude-opus-latest","max_tokens":32000,"system_prompt":"You are a specialist writer. Revise the draft on each reviewer finding."}""";

    private static readonly Dictionary<string, string> AliasMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-latest"] = "claude-opus-5",
        ["claude-sonnet-latest"] = "claude-sonnet-5",
    };

    private static CrewSpecValidator MakeValidator(ICrewService crewService) =>
        new(crewService,
            new ResolvingModelCatalog(AliasMap),
            new AnyGroundingFactory(),
            new EmptyToolRepo(),
            new InMemorySpecializationPackRepository(),
            new AnyResolver());

    private static string SpecWith(string reviewerJson) =>
        $$"""
        {
            "mode": "composed",
            "executor": {{AliasedExecutor}},
            "reviewers": [{{reviewerJson}}],
            "finalizers": [{"reuse": "file-export"}]
        }
        """;

    private static bool HasPluralityIssue(IReadOnlyList<CrewSpecValidationIssue> issues) =>
        issues.Any(i => i.Message.Contains("same model as the executor", StringComparison.Ordinal));

    [Fact]
    public async Task WarnsWhenAnInlineReviewerResolvesToTheExecutorModel()
    {
        var reviewer = """{"name":"r","provider":"claude-cli","model":"claude-opus-5","max_tokens":8000,"system_prompt":"Review."}""";

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(SpecWith(reviewer));

        Assert.True(HasPluralityIssue(issues), "alias and concrete id name the same model");
        Assert.All(issues.Where(i => i.Message.Contains("same model as the executor", StringComparison.Ordinal)),
            i => Assert.False(i.IsCritical));
    }

    [Fact]
    public async Task DoesNotWarnWhenTheReviewerResolvesToADifferentModel()
    {
        var reviewer = """{"name":"r","provider":"claude-cli","model":"claude-sonnet-latest","max_tokens":8000,"system_prompt":"Review."}""";

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(SpecWith(reviewer));

        Assert.False(HasPluralityIssue(issues));
    }

    [Fact]
    public async Task DoesNotWarnAcrossDifferentProviders()
    {
        var reviewer = """{"name":"r","provider":"codex-cli","model":"claude-opus-5","max_tokens":8000,"system_prompt":"Review."}""";

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(SpecWith(reviewer));

        Assert.False(HasPluralityIssue(issues));
    }

    [Fact]
    public async Task WarnsForAReusedReviewerToo()
    {
        var crewService = new NoProfilesCrewService
        {
            Reviewer = new ReviewerProfile("clarity", "Clarity", "d", "p", "claude-cli", "claude-opus-latest", 8000, true),
        };

        var issues = await MakeValidator(crewService).ValidateAsync(SpecWith("""{"reuse":"clarity"}"""));

        Assert.True(HasPluralityIssue(issues));
    }

    [Fact]
    public async Task DoesNotWarnForAReusedReviewerOnAnotherModel()
    {
        var crewService = new NoProfilesCrewService
        {
            Reviewer = new ReviewerProfile("clarity", "Clarity", "d", "p", "openrouter", "openai/gpt-5.6-sol", 8000, true),
        };

        var issues = await MakeValidator(crewService).ValidateAsync(SpecWith("""{"reuse":"clarity"}"""));

        Assert.False(HasPluralityIssue(issues));
    }

    private sealed class NoProfilesCrewService : StubCrewServiceBase
    {
        public ReviewerProfile? Reviewer { get; init; }

        public override Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Reviewer is not null && Reviewer.Name == name ? Reviewer : null);
    }

    private sealed class AnyGroundingFactory : IGroundingProviderFactory
    {
        public IGroundingProvider Create(string providerType) => throw new NotSupportedException();
        public bool IsRegistered(string providerType) => true;
        public IReadOnlyCollection<string> RegisteredTypes => ["tavily"];
    }

    private sealed class EmptyToolRepo : IToolDefinitionRepository
    {
        public Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<ToolDefinition?>(null);
        public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        public Task<IReadOnlyList<ToolDefinition>> GetSystemToolsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        public Task<IReadOnlyList<ToolDefinition>> GetCustomToolsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        public Task UpsertAsync(ToolDefinition tool, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AnyResolver : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)
            => throw new NotSupportedException();
        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
            => throw new NotSupportedException();
        public bool SupportsAgenticTools(string providerName) => true;
        public bool SupportsStructuredOutputs(string providerName) => true;
    }
}
