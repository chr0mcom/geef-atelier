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
/// Step 10 of the validator: a reused reviewer with a manual binding on the executor's model turns the crew
/// into a monoculture reviewing itself (managed reviewers are assigned by the model matrix at run start). Because an always-latest alias and the concrete id it points at are
/// the same model under two names, both sides must be compared after resolution — comparing the raw
/// strings would let exactly that case through.
/// </summary>
public sealed class CrewSpecValidatorPluralityTests
{
    private const string AliasedExecutor =
        """{"name":"task-executor","model_slot":"Executor","max_tokens":32000,"system_prompt":"You are a specialist writer. Revise the draft on each reviewer finding."}""";

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

    private const string ReusedManualExecutor = """{"reuse":"custom-e"}""";

    private static string SpecWith(string reviewerJson, string executorJson = AliasedExecutor) =>
        $$"""
        {
            "mode": "composed",
            "executor": {{executorJson}},
            "reviewers": [{{reviewerJson}}],
            "finalizers": [{"reuse": "file-export"}]
        }
        """;

    private static bool HasPluralityIssue(IReadOnlyList<CrewSpecValidationIssue> issues) =>
        issues.Any(i => i.Message.Contains("executor's model", StringComparison.Ordinal));

    [Fact]
    public async Task WarnsForAReusedManualReviewerWhoseConcreteIdIsTheManualExecutorsAlias()
    {
        var crewService = new NoProfilesCrewService
        {
            Executor = new ExecutorProfile("custom-e", "E", "d", "p", "claude-cli", "claude-opus-latest", 32000, false),
            Reviewer = new ReviewerProfile("custom-r", "R", "d", "p", "claude-cli", "claude-opus-5", 8000, false),
        };

        var issues = await MakeValidator(crewService).ValidateAsync(SpecWith("""{"reuse":"custom-r"}""", ReusedManualExecutor));

        Assert.True(HasPluralityIssue(issues), "alias and concrete id name the same model");
        Assert.All(issues.Where(i => i.Message.Contains("executor's model", StringComparison.Ordinal)), i => Assert.False(i.IsCritical));
    }

    [Fact]
    public async Task ManagedInlineExecutorIsLeftToTheRunTimeAssignment()
    {
        var crewService = new NoProfilesCrewService
        {
            Reviewer = new ReviewerProfile("custom-r", "R", "d", "p", "claude-cli", "claude-opus-5", 8000, false),
        };

        var issues = await MakeValidator(crewService).ValidateAsync(SpecWith("""{"reuse":"custom-r"}"""));

        Assert.False(HasPluralityIssue(issues));
    }

    [Fact]
    public async Task ManagedReusedExecutorIsLeftToTheRunTimeAssignment()
    {
        var crewService = new NoProfilesCrewService
        {
            Executor = new ExecutorProfile("custom-e", "E", "d", "p", "claude-cli", "claude-opus-latest", 32000, false,
                ModelSlot: Geef.Atelier.Core.Domain.Crew.Models.ModelSlot.Executor),
            Reviewer = new ReviewerProfile("custom-r", "R", "d", "p", "claude-cli", "claude-opus-5", 8000, false),
        };

        var issues = await MakeValidator(crewService).ValidateAsync(SpecWith("""{"reuse":"custom-r"}""", ReusedManualExecutor));

        Assert.False(HasPluralityIssue(issues));
    }

    [Fact]
    public async Task InlineReviewersAreManagedAndNeverFlagged()
    {
        var reviewer = """{"name":"r","model_slot":"FactReviewer","provider":"claude-cli","model":"claude-opus-5","max_tokens":8000,"system_prompt":"Review."}""";

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(SpecWith(reviewer));

        Assert.False(HasPluralityIssue(issues));
    }

    [Fact]
    public async Task DoesNotWarnForAReusedManagedReviewer()
    {
        var crewService = new NoProfilesCrewService
        {
            Reviewer = new ReviewerProfile("clarity", "Clarity", "d", "p", "claude-cli", "claude-opus-latest", 8000, true,
                ModelSlot: Geef.Atelier.Core.Domain.Crew.Models.ModelSlot.StyleReviewer),
        };

        var issues = await MakeValidator(crewService).ValidateAsync(SpecWith("""{"reuse":"clarity"}"""));

        Assert.False(HasPluralityIssue(issues));
    }

    [Fact]
    public async Task DoesNotWarnForAReusedManualReviewerOnAnotherModel()
    {
        var crewService = new NoProfilesCrewService
        {
            Reviewer = new ReviewerProfile("custom-r", "R", "d", "p", "codex-cli", "gpt-6.2-sol", 8000, false),
        };

        var issues = await MakeValidator(crewService).ValidateAsync(SpecWith("""{"reuse":"custom-r"}"""));

        Assert.False(HasPluralityIssue(issues));
    }

    [Fact]
    public async Task UnknownModelSlotIsCritical()
    {
        var reviewer = """{"name":"r","model_slot":"Proofreader","max_tokens":8000,"system_prompt":"Review."}""";

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(SpecWith(reviewer));

        Assert.Contains(issues, i => i.Field == "reviewers[0].model_slot" && i.IsCritical && i.Message.Contains("FactReviewer"));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("[\"FactReviewer\"]")]
    [InlineData("\"FactReviewer,StyleReviewer\"")]
    public async Task NonStringOrCombinedModelSlotIsCritical(string slotJson)
    {
        var reviewer = $$"""{"name":"r","model_slot":{{slotJson}},"max_tokens":8000,"system_prompt":"Review."}""";

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(SpecWith(reviewer));

        Assert.Contains(issues, i => i.Field == "reviewers[0].model_slot" && i.IsCritical);
    }

    [Fact]
    public async Task SlotOfAnotherActorKindIsCritical()
    {
        var reviewer = """{"name":"r","model_slot":"Executor","max_tokens":8000,"system_prompt":"Review."}""";

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(SpecWith(reviewer));

        Assert.Contains(issues, i => i.Field == "reviewers[0].model_slot" && i.IsCritical);
    }

    [Fact]
    public async Task MissingSlotOnlyWarnsAndInlinePartsNeedNoProviderOrModel()
    {
        var reviewer = """{"name":"r","max_tokens":8000,"system_prompt":"Review."}""";

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(SpecWith(reviewer));

        Assert.Contains(issues, i => i.Field == "reviewers[0].model_slot" && !i.IsCritical && i.Message.Contains("DomainReviewer"));
        Assert.DoesNotContain(issues, i => i.Field.EndsWith(".provider") || i.Field.EndsWith(".model"));
        Assert.DoesNotContain(issues, i => i.IsCritical && i.Field.StartsWith("reviewers"));
    }

    [Fact]
    public async Task NonCliClaudeOrOpenAiRouteIsReportedAsRewritten()
    {
        var reviewer = """{"name":"r","model_slot":"FactReviewer","provider":"openrouter","model":"openai/gpt-5.6-sol","max_tokens":8000,"system_prompt":"Review."}""";

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(SpecWith(reviewer));

        var issue = Assert.Single(issues, i => i.Field == "reviewers[0].provider");
        Assert.False(issue.IsCritical);
        Assert.Contains("codex-cli/gpt-sol-latest", issue.Message);
    }

    [Fact]
    public async Task OtherVendorRoutesAreNotReported()
    {
        var reviewer = """{"name":"r","model_slot":"FactReviewer","provider":"xai","model":"grok-4.5","max_tokens":8000,"system_prompt":"Review."}""";

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(SpecWith(reviewer));

        Assert.DoesNotContain(issues, i => i.Field == "reviewers[0].provider");
    }

    [Fact]
    public async Task InlineRestApiGroundingAndExternalSinkFinalizersAreCritical()
    {
        var spec = $$$"""
        {
            "mode": "composed",
            "executor": {{{AliasedExecutor}}},
            "reviewers": [{"name":"r","model_slot":"FactReviewer","max_tokens":8000,"system_prompt":"Review."}],
            "grounding_providers": [{"name":"api","provider_type":"rest-api","settings":{"url":"https://example.invalid","authHeaderEnv":"CLI_PROXY_API_KEY"}}],
            "finalizers": [{"name":"hook","finalizer_type":"ExternalSink","settings":{"SinkKind":"Webhook","Url":"http://10.0.0.1/"}}]
        }
        """;

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(spec);

        Assert.Contains(issues, i => i.Field == "grounding_providers[0].provider_type" && i.IsCritical && i.Message.Contains("rest-api"));
        Assert.Contains(issues, i => i.Field == "grounding_providers[0].settings" && !i.IsCritical && i.Message.Contains("authHeaderEnv"));
        Assert.Contains(issues, i => i.Field == "finalizers[0].finalizer_type" && i.IsCritical);
    }

    [Fact]
    public async Task DroppedTransformSettingsAreReportedAsWarnings()
    {
        var spec = $$$"""
        {
            "mode": "composed",
            "executor": {{{AliasedExecutor}}},
            "reviewers": [{"name":"r","model_slot":"FactReviewer","max_tokens":8000,"system_prompt":"Review."}],
            "finalizers": [{"name":"t","finalizer_type":"Transform","system_prompt":"Polish.","settings":{"Provider":"openrouter","Model":"openai/gpt-4o","SystemPrompt":"Polish."}}]
        }
        """;

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(spec);

        var issue = Assert.Single(issues, i => i.Field == "finalizers[0].settings");
        Assert.False(issue.IsCritical);
        Assert.Contains("Model, Provider", issue.Message);
    }

    [Theory]
    [InlineData(" Transform ")]
    [InlineData("transform")]
    public async Task TransformIsRecognizedAfterParsingAndNeedsAPrompt(string type)
    {
        var spec = $$$"""
        {
            "mode": "composed",
            "executor": {{{AliasedExecutor}}},
            "reviewers": [{"name":"r","model_slot":"FactReviewer","max_tokens":8000,"system_prompt":"Review."}],
            "finalizers": [{"name":"t","finalizer_type":"{{{type}}}","model_slot":"FactReviewer"}]
        }
        """;

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(spec);

        Assert.Contains(issues, i => i.Field == "finalizers[0].system_prompt");
        Assert.Contains(issues, i => i.Field == "finalizers[0].model_slot" && i.IsCritical);
    }

    [Fact]
    public async Task UnknownFinalizerTypeIsCritical()
    {
        var spec = $$$"""
        {
            "mode": "composed",
            "executor": {{{AliasedExecutor}}},
            "reviewers": [{"name":"r","model_slot":"FactReviewer","max_tokens":8000,"system_prompt":"Review."}],
            "finalizers": [{"name":"t","finalizer_type":"Transfrom","system_prompt":"Polish."}]
        }
        """;

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(spec);

        Assert.Contains(issues, i => i.Field == "finalizers[0].finalizer_type" && i.IsCritical && i.Message.Contains("Transfrom"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("\"Lega\"", true)]
    [InlineData("\"Legal\"", false)]
    public async Task MissingOrInvalidModelDomainOnlyWarns(string? modelDomainJson, bool warns)
    {
        var modelDomain = modelDomainJson is null ? "" : $"\"model_domain\": {modelDomainJson},";
        var spec = $$$"""
        {
            "mode": "composed",
            {{{modelDomain}}}
            "executor": {{{AliasedExecutor}}},
            "reviewers": [{"name":"r","model_slot":"FactReviewer","max_tokens":8000,"system_prompt":"Review."}],
            "finalizers": [{"reuse": "file-export"}]
        }
        """;

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(spec);

        Assert.Equal(warns, issues.Any(i => i.Field == "model_domain"));
        Assert.DoesNotContain(issues, i => i.Field == "model_domain" && i.IsCritical);
    }

    [Fact]
    public async Task InlineUrlFetchGroundingIsDroppedWithAWarning()
    {
        var spec = $$$"""
        {
            "mode": "composed",
            "model_domain": "General",
            "executor": {{{AliasedExecutor}}},
            "reviewers": [{"name":"r","model_slot":"FactReviewer","max_tokens":8000,"system_prompt":"Review."}],
            "grounding_providers": [{"name":"fetch","provider_type":"url-fetch","settings":{"urls":"http://169.254.169.254/"}}],
            "finalizers": [{"reuse": "file-export"}]
        }
        """;

        var issues = await MakeValidator(new NoProfilesCrewService()).ValidateAsync(spec);

        var issue = Assert.Single(issues, i => i.Field == "grounding_providers[0].provider_type");
        Assert.False(issue.IsCritical);
        Assert.Contains("dropped", issue.Message);
    }

    private sealed class NoProfilesCrewService : StubCrewServiceBase
    {
        public ReviewerProfile? Reviewer { get; init; }

        public ExecutorProfile? Executor { get; init; }

        public override Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult(Executor is not null && Executor.Name == name ? Executor : null);

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
