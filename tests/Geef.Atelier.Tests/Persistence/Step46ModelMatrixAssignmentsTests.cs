using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Runs the Step46 statements against a real database (D-066): known custom rows get their role and the matrix
/// display binding, unknown rows only obey the CLI-route rule, and the frozen literals stay parseable.
/// </summary>
[Collection("Postgres")]
public sealed class Step46ModelMatrixAssignmentsTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ClassifiesKnownCustomRowsAndRebindsThemToTheMatrix()
    {
        await using var db = fixture.NewContext();
        await SeedAsync(db, "ReviewerProfiles", "custom-fact-fidelity-reviewer", "openrouter", "x-ai/grok-4.5");
        await SeedAsync(db, "ReviewerProfiles", "custom-legal-positioning-reviewer", "openrouter", "anthropic/claude-opus-5");
        await SeedAsync(db, "ReviewerProfiles", "custom-emotional-authenticity-reviewer", "claude-cli", "claude-fable-latest");
        await SeedAsync(db, "ReviewerProfiles", "learning-generalizability", "claude-cli", "claude-haiku-latest", isSystem: true);
        await SeedAsync(db, "ExecutorProfiles", "custom-dotnetpro-article-writer", "claude-cli", "claude-fable-latest");
        await SeedAsync(db, "ExecutorProfiles", "custom-luna-kulanz-letter-executor", "claude-cli", "claude-fable-latest");

        await RunUpAsync(db);

        await using var verify = fixture.NewContext();
        await AssertReviewerAsync(verify, "custom-fact-fidelity-reviewer", ModelSlot.FactReviewer, "codex-cli", "gpt-astra-latest");
        await AssertReviewerAsync(verify, "custom-legal-positioning-reviewer", ModelSlot.DomainReviewer, "openrouter", "google/gemini-4-neon");
        await AssertReviewerAsync(verify, "custom-emotional-authenticity-reviewer", ModelSlot.StyleReviewer, "codex-cli", "gpt-sol-latest");
        await AssertReviewerAsync(verify, "learning-generalizability", ModelSlot.Helper, "claude-cli", "claude-haiku-latest");

        var dotnetpro = await verify.Set<ExecutorProfile>().AsNoTracking().SingleAsync(p => p.Name == "custom-dotnetpro-article-writer");
        Assert.Equal((ModelSlot.Executor, (ModelDomain?)null, "claude-fable-latest"), (dotnetpro.ModelSlot!.Value, dotnetpro.ModelDomain, dotnetpro.Model));
        var kulanz = await verify.Set<ExecutorProfile>().AsNoTracking().SingleAsync(p => p.Name == "custom-luna-kulanz-letter-executor");
        Assert.Equal("claude-opus-latest", kulanz.Model);
    }

    [Fact]
    public async Task UnknownRowsStayManualAndOnlyObeyTheCliRoute()
    {
        await using var db = fixture.NewContext();
        await SeedAsync(db, "ReviewerProfiles", "s46-unknown-sonnet", "openrouter", "anthropic/claude-sonnet-5");
        await SeedAsync(db, "ReviewerProfiles", "s46-unknown-luna", "codex-cli", "gpt-5.6-luna");
        await SeedAsync(db, "ReviewerProfiles", "s46-unknown-sol", "openrouter", "openai/gpt-6.2-sol");
        await SeedAsync(db, "ReviewerProfiles", "s46-unknown-old-gpt", "openai-direct", "gpt-4o-mini");
        await SeedAsync(db, "ReviewerProfiles", "s46-unknown-grok", "xai", "grok-4.5");
        await SeedAsync(db, "AdvisorProfiles", "s46-unknown-advisor", "openrouter", "anthropic/claude-fable-5.2");
        await SeedAsync(db, "ReviewerProfiles", "s46-unknown-old-sol", "codex-cli", "gpt-5.6-sol");
        await SeedAsync(db, "ReviewerProfiles", "s46-unknown-o3", "openai-direct", "o3");
        await SeedAsync(db, "ReviewerProfiles", "s46-unknown-oss-local", "ollama-local", "gpt-oss:20b");
        await SeedAsync(db, "ReviewerProfiles", "s46-unknown-oss-router", "openrouter", "openai/gpt-oss-120b");

        await RunUpAsync(db);

        await using var verify = fixture.NewContext();
        await AssertReviewerAsync(verify, "s46-unknown-sonnet", null, "claude-cli", "claude-sonnet-latest");
        await AssertReviewerAsync(verify, "s46-unknown-luna", null, "codex-cli", "gpt-astra-latest");
        await AssertReviewerAsync(verify, "s46-unknown-sol", null, "codex-cli", "gpt-6.2-sol");
        await AssertReviewerAsync(verify, "s46-unknown-old-gpt", null, "codex-cli", "gpt-astra-latest");
        await AssertReviewerAsync(verify, "s46-unknown-old-sol", null, "codex-cli", "gpt-sol-latest");
        await AssertReviewerAsync(verify, "s46-unknown-o3", null, "codex-cli", "gpt-astra-latest");
        await AssertReviewerAsync(verify, "s46-unknown-oss-local", null, "ollama-local", "gpt-oss:20b");
        await AssertReviewerAsync(verify, "s46-unknown-oss-router", null, "openrouter", "openai/gpt-oss-120b");
        await AssertReviewerAsync(verify, "s46-unknown-grok", null, "xai", "grok-4.5");
        var advisor = await verify.Set<Geef.Atelier.Core.Domain.Crew.Advisors.AdvisorProfile>().AsNoTracking().SingleAsync(p => p.Name == "s46-unknown-advisor");
        Assert.Equal(("claude-cli", "claude-fable-latest", (ModelSlot?)null), (advisor.Provider, advisor.Model, advisor.ModelSlot));
    }

    [Fact]
    public async Task CustomRefinersBecomeManagedAndEverythingElseIsLeftAlone()
    {
        await using var db = fixture.NewContext();
        await SeedGroundingAsync(db, "s46-refined", """{"Tier":"advanced","refinementMode":"0","refinementProvider":"openrouter","refinementModel":"minimax/minimax-m3"}""", isSystem: false);
        await SeedGroundingAsync(db, "s46-plain", """{"Tier":"basic"}""", isSystem: false);
        await SeedGroundingAsync(db, "s46-system", """{"refinementProvider":"deepseek","refinementModel":"deepseek/deepseek-v4-flash"}""", isSystem: true);

        await RunUpAsync(db);

        await using var verify = fixture.NewContext();
        var refined = await GroundingAsync(verify, "s46-refined");
        Assert.Equal("claude-cli", refined.ProviderSettings[GroundingProviderProfile.KeyRefinementProvider]);
        Assert.Equal("claude-sonnet-latest", refined.ProviderSettings[GroundingProviderProfile.KeyRefinementModel]);
        Assert.Equal(ModelSlot.GroundingRefiner, refined.RefinementModelSlot);
        Assert.Equal("advanced", refined.ProviderSettings["Tier"]);

        Assert.False((await GroundingAsync(verify, "s46-plain")).ProviderSettings.ContainsKey(GroundingProviderProfile.KeyRefinementModel));
        var system = await GroundingAsync(verify, "s46-system");
        Assert.Equal(("claude-cli", "claude-sonnet-latest", "GroundingRefiner"),
            (system.ProviderSettings[GroundingProviderProfile.KeyRefinementProvider],
             system.ProviderSettings[GroundingProviderProfile.KeyRefinementModel],
             system.ProviderSettings[GroundingProviderProfile.KeyModelSlot]));
    }

    [Fact]
    public async Task FinalizerBindingsFollowTheCliRouteAndSystemTransformsBecomeManaged()
    {
        await using var db = fixture.NewContext();
        await SeedFinalizerAsync(db, "anti-ai-voice", "Transform", """{"Provider":"codex-cli","Model":"gpt-5.6-sol","SystemPrompt":"p"}""", isSystem: true);
        await SeedFinalizerAsync(db, "glossary", "Transform", """{"Provider":"codex-cli","Model":"gpt-5.6-sol","SystemPrompt":"p"}""", isSystem: true);
        await SeedFinalizerAsync(db, "s46-custom-transform", "Transform", """{"Provider":"openrouter","Model":"openai/gpt-4o-mini","SystemPrompt":"p"}""", isSystem: false);
        await SeedFinalizerAsync(db, "s46-custom-claude", "Transform", """{"Provider":"openrouter","Model":"anthropic/claude-sonnet-4-5","SystemPrompt":"p"}""", isSystem: false);
        await SeedFinalizerAsync(db, "s46-custom-learning", "LearningExtract", """{"provider":"openrouter","model":"openai/gpt-5.6-sol"}""", isSystem: false);
        await SeedFinalizerAsync(db, "s46-custom-oss", "Transform", """{"Provider":"ollama-local","Model":"gpt-oss:20b","SystemPrompt":"p"}""", isSystem: false);

        await RunUpAsync(db);

        await using var verify = fixture.NewContext();
        Assert.Equal(("codex-cli", "gpt-sol-latest", "TransformRewrite"), await FinalizerBindingAsync(verify, "anti-ai-voice", "Provider", "Model"));
        Assert.Equal(("claude-cli", "claude-sonnet-latest", "TransformCondense"), await FinalizerBindingAsync(verify, "glossary", "Provider", "Model"));
        Assert.Equal(("codex-cli", "gpt-astra-latest", (string?)null), await FinalizerBindingAsync(verify, "s46-custom-transform", "Provider", "Model"));
        Assert.Equal(("claude-cli", "claude-sonnet-latest", (string?)null), await FinalizerBindingAsync(verify, "s46-custom-claude", "Provider", "Model"));
        Assert.Equal(("codex-cli", "gpt-sol-latest", (string?)null), await FinalizerBindingAsync(verify, "s46-custom-learning", "provider", "model"));
        Assert.Equal(("ollama-local", "gpt-oss:20b", (string?)null), await FinalizerBindingAsync(verify, "s46-custom-oss", "Provider", "Model"));
    }

    public static TheoryData<string, string> CliRouteParityCases => new()
    {
        { "openai-direct", "o3" },
        { "codex-cli", "o3" },
        { "codex-cli", "o4-mini" },
        { "codex-cli", "gpt-5.6-sol" },
        { "codex-cli", "gpt-5.6-terra" },
        { "codex-cli", "gpt-6-luna" },
        { "codex-cli", "gpt-6.2-sol-mini" },
        { "codex-cli", "gpt-6.3-sol" },
        { "codex-cli", "gpt-6.2-astra" },
        { "codex-cli", "gpt-sol-latest" },
        { "openrouter", "openai/gpt-6.2-sol" },
        { "openrouter", "openai/gpt-6.3-sol" },
        { "openrouter", "openai/gpt-4o" },
        { "openrouter", "openai/gpt-oss-120b" },
        { "ollama-local", "gpt-oss:20b" },
        { "openai-direct", "gpt-5.6-terra" },
        { "openrouter", "anthropic/claude-sonnet-5" },
        { "openrouter", "anthropic/claude-fable-5.2" },
        { "claude-cli", "claude-opus-5" },
        { "xai", "grok-4.5" },
        { "openrouter", " openai/gpt-6.2-sol " },
        { "claude-cli", "gpt-6.2-sol" },
        { "codex-cli", "claude-sonnet-latest" },
        { "openrouter", " anthropic/claude-opus-5" },
        { "openrouter", "\tanthropic/claude-opus-5" },
        { "openrouter", "\topenai/gpt-6.2-sol\n" },
        { "\topenrouter", "openai/gpt-5.6-sol" },
        { "openrouter", "openai/gpt-6.2-sol-dev" },
        { "openrouter", "\vopenai/gpt-6.2-sol\f" },
    };

    [Theory]
    [MemberData(nameof(CliRouteParityCases))]
    public async Task StepFortySixRewritesProfilesExactlyLikeCliRoute(string provider, string model)
    {
        await using var db = fixture.NewContext();
        var name = $"s46-parity-{provider}-{model}".Replace('/', '-').Replace(':', '-').Replace('.', '-').Replace(' ', '_').Replace('\t', '_').Replace('\n', '_').Replace('\v', '_').Replace('\f', '_');
        await SeedAsync(db, "ReviewerProfiles", name, provider, model);

        await RunUpAsync(db);

        await using var verify = fixture.NewContext();
        var expected = CliRoute.Normalize(provider, model);
        await AssertReviewerAsync(verify, name, null, expected.Provider, expected.Model);
    }

    [Theory]
    [MemberData(nameof(CliRouteParityCases))]
    public async Task StepFortySixRewritesFinalizerBindingsExactlyLikeCliRoute(string provider, string model)
    {
        await using var db = fixture.NewContext();
        var name = $"s46-fparity-{provider}-{model}".Replace('/', '-').Replace(':', '-').Replace('.', '-').Replace(' ', '_').Replace('\t', '_').Replace('\n', '_').Replace('\v', '_').Replace('\f', '_');
        var settingsJson = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["Provider"] = provider, ["Model"] = model, ["SystemPrompt"] = "p" });
        await SeedFinalizerAsync(db, name, "Transform", settingsJson, isSystem: false);

        await RunUpAsync(db);

        await using var verify = fixture.NewContext();
        var expected = CliRoute.Normalize(provider, model);
        Assert.Equal((expected.Provider, expected.Model, (string?)null), await FinalizerBindingAsync(verify, name, "Provider", "Model"));
    }

    [Fact]
    public async Task SetsTheCrewDomainOfKnownTemplates()
    {
        await using var db = fixture.NewContext();
        await ExecAsync(db,
            """
            DELETE FROM "CrewTemplates" WHERE "Name" = 'custom-matzes-redeschreiber';
            INSERT INTO "CrewTemplates" ("Name","DisplayName","Description","ExecutorProfileName","ReviewerProfileNames","EvaluationStrategy","AdvisorProfileNames","IsSystem","GroundingProviderNames","FinalizerProfileNames","RunFinalizersOnMaxAttempts","ActorPackBindings")
            VALUES ('custom-matzes-redeschreiber','Rede','d','custom-afd-sachsen-anhalt-speechwriter','[]'::jsonb,'Parallel','[]'::jsonb,false,'[]'::jsonb,'[]'::jsonb,false,'{}'::jsonb);
            """);

        await RunUpAsync(db);

        await using var verify = fixture.NewContext();
        var template = await verify.Set<CrewTemplate>().AsNoTracking().SingleAsync(t => t.Name == "custom-matzes-redeschreiber");
        Assert.Equal(ModelDomain.Speech, template.Domain);
    }

    [Fact]
    public async Task IsIdempotentAndKeepsLaterManualChanges()
    {
        await using var db = fixture.NewContext();
        await SeedAsync(db, "ReviewerProfiles", "custom-cdu-opposition-reviewer", "codex-cli", "gpt-5.6-sol");
        await RunUpAsync(db);
        await db.Database.ExecuteSqlRawAsync("""UPDATE "ReviewerProfiles" SET "Model" = 'claude-sonnet-latest', "Provider" = 'claude-cli' WHERE "Name" = 'custom-cdu-opposition-reviewer';""");

        await RunUpAsync(db);

        await using var verify = fixture.NewContext();
        await AssertReviewerAsync(verify, "custom-cdu-opposition-reviewer", ModelSlot.DomainReviewer, "claude-cli", "claude-sonnet-latest");
    }

    [Fact]
    public async Task UnreadableSlotValuesAreReadAsManualInsteadOfBreakingTheListing()
    {
        await using var db = fixture.NewContext();
        await SeedAsync(db, "ReviewerProfiles", "s46-bogus", "codex-cli", "gpt-6.2-sol");
        await db.Database.ExecuteSqlRawAsync("""UPDATE "ReviewerProfiles" SET "ModelSlot" = 'FactReviwer', "ModelDomain" = '7' WHERE "Name" = 's46-bogus';""");
        await SeedAsync(db, "ReviewerProfiles", "s46-bogus-2", "codex-cli", "gpt-6.2-sol");
        await db.Database.ExecuteSqlRawAsync("""UPDATE "ReviewerProfiles" SET "ModelSlot" = 'FactReviewer,StyleReviewer', "ModelDomain" = '+2' WHERE "Name" = 's46-bogus-2';""");

        await using var verify = fixture.NewContext();
        var profile = await verify.Set<ReviewerProfile>().AsNoTracking().SingleAsync(p => p.Name == "s46-bogus");
        Assert.Null(profile.ModelSlot);
        Assert.Null(profile.ModelDomain);
        var combined = await verify.Set<ReviewerProfile>().AsNoTracking().SingleAsync(p => p.Name == "s46-bogus-2");
        Assert.Null(combined.ModelSlot);
        Assert.Null(combined.ModelDomain);
    }

    [Fact]
    public void FrozenLiteralsAreValidEnumNamesAndMatrixCandidates()
    {
        foreach (var (template, domain) in Step46ModelMatrixAssignments.TemplateDomains)
        {
            Assert.StartsWith(template == "learning-evaluation" ? "learning-" : "custom-", template);
            Assert.True(ModelDomainParser.TryParseExact(domain) is not null, domain);
        }

        var domains = Step46ModelMatrixAssignments.TemplateDomains.ToDictionary(t => t.Template, t => ModelDomainParser.TryParseExact(t.Domain)!.Value);
        foreach (var (table, name, slotName, provider, model) in Step46ModelMatrixAssignments.Classification)
        {
            Assert.True(ModelSlotParser.TryParse(slotName, out var slot), slotName);
            var kind = table switch
            {
                "ExecutorProfiles" => ModelActorKind.Executor,
                "ReviewerProfiles" => ModelActorKind.Reviewer,
                _ => ModelActorKind.Advisor,
            };
            Assert.True(slot.IsAllowedFor(kind), $"{name}: {slot} on {table}");
            Assert.Contains(new ModelCandidate(provider, model), Enum.GetValues<ModelDomain>().SelectMany(d => ModelMatrix.CandidatesFor(slot, d)));
        }

        Assert.Equal(Step46ModelMatrixAssignments.Classification.Length,
            Step46ModelMatrixAssignments.Classification.Select(c => c.Name).Distinct().Count());
        Assert.Contains(domains, d => d.Value == ModelDomain.Speech);
    }

    [Fact]
    public void DownOnlyDropsTheAddedColumns()
    {
        var down = new Step46ModelMatrixAssignments().DownOperations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

        Assert.All(down, sql => Assert.Contains("DROP COLUMN IF EXISTS", sql));
        Assert.DoesNotContain(down, sql => sql.Contains("UPDATE", StringComparison.Ordinal));
    }

    private static async Task RunUpAsync(AtelierDbContext db)
    {
        foreach (var sql in new Step46ModelMatrixAssignments().UpOperations.OfType<SqlOperation>().Select(o => o.Sql))
            await ExecAsync(db, sql);
    }

    private static Task ExecAsync(AtelierDbContext db, string sql) =>
        db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}"));

    private static Task SeedAsync(AtelierDbContext db, string table, string name, string provider, string model, bool isSystem = false)
    {
        var extra = table == "AdvisorProfiles" ? ",\"Mode\",\"Trigger\"" : "";
        var extraValues = table == "AdvisorProfiles" ? ",'Strategic','BeforeFirstExecution'" : "";
        var sql = $"""
            DELETE FROM "{table}" WHERE "Name" = '{name}';
            INSERT INTO "{table}" ("Name","DisplayName","Description","SystemPrompt","Provider","Model","MaxTokens","IsSystem","ToolNames"{extra})
            VALUES ('{name}','{name}','step46 seed','p','{provider}','{model}',8000,{(isSystem ? "true" : "false")},'[]'::jsonb{extraValues});
            """;
        return ExecAsync(db, sql);
    }

    private static Task SeedGroundingAsync(AtelierDbContext db, string name, string settingsJson, bool isSystem)
    {
        var sql = $"""
            DELETE FROM "GroundingProviderProfiles" WHERE "Name" = '{name}';
            INSERT INTO "GroundingProviderProfiles" ("Name","DisplayName","Description","ProviderType","ProviderSettings","IsSystem")
            VALUES ('{name}','{name}','step46 seed','tavily','{settingsJson}'::jsonb,{(isSystem ? "true" : "false")});
            """;
        return ExecAsync(db, sql);
    }

    private static Task SeedFinalizerAsync(AtelierDbContext db, string name, string type, string settingsJson, bool isSystem)
    {
        var sql = $"""
            DELETE FROM "FinalizerProfiles" WHERE "Name" = '{name}';
            INSERT INTO "FinalizerProfiles" ("Name","DisplayName","Description","FinalizerType","Settings","IsSystem","CreatedAt","UpdatedAt","ToolNames")
            VALUES ('{name}','{name}','step46 seed','{type}','{settingsJson}'::jsonb,{(isSystem ? "true" : "false")},now(),now(),'[]'::jsonb);
            """;
        return ExecAsync(db, sql);
    }

    private static async Task<(string Provider, string Model, string? Slot)> FinalizerBindingAsync(
        AtelierDbContext db, string name, string providerKey, string modelKey)
    {
        var profile = await db.Set<Geef.Atelier.Core.Domain.Crew.Finalizers.FinalizerProfile>().AsNoTracking().SingleAsync(p => p.Name == name);
        return (profile.Settings[providerKey], profile.Settings[modelKey], profile.Settings.GetValueOrDefault("ModelSlot"));
    }

    private static Task<GroundingProviderProfile> GroundingAsync(AtelierDbContext db, string name) =>
        db.Set<GroundingProviderProfile>().AsNoTracking().SingleAsync(p => p.Name == name);

    private static async Task AssertReviewerAsync(AtelierDbContext db, string name, ModelSlot? slot, string provider, string model)
    {
        var profile = await db.Set<ReviewerProfile>().AsNoTracking().SingleAsync(p => p.Name == name);
        Assert.Equal((slot, provider, model), (profile.ModelSlot, profile.Provider, profile.Model));
    }
}
