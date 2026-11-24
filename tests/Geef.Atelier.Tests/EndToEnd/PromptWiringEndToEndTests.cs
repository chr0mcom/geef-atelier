using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Proves over the real wiring that grounding content (attachments, web search, several providers)
/// actually reaches the executor prompt the cli-proxy receives.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class PromptWiringEndToEndTests(PostgresFixture postgres)
{
    private const string AttachmentMarker = "E2E-MARKER-ANHANG-7F3C";
    private const string WebMarker = "E2E-MARKER-WEB-91AB";
    private const string AlphaMarker = "E2E-MARKER-ALPHA-25D1";
    private const string BetaMarker = "E2E-MARKER-BETA-C84E";

    private const string LighthouseBriefing =
        "Write a short factual summary about the history of the Nordkap lighthouse for a visitor leaflet.";

    [Fact]
    [Trait(EndToEndCollection.StageTrait, EndToEndCollection.AfterRepair)]
    public async Task Attachment_content_reaches_the_executor_prompt()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);

        var runId = await host.SubmitAsync(new SubmitRunRequest(
            BriefingText: LighthouseBriefing,
            ConfigJson: "{}",
            CrewTemplateName: SystemCrew.KlassikTemplateName,
            Attachments: [LighthouseNotes()]));
        await host.WaitForTerminalAsync(runId);

        Assert.True(
            host.Llm.AnyExecutorPromptContains(AttachmentMarker),
            $"Attachment marker missing from every executor prompt.\n{host.Llm.Describe()}");
    }

    [Fact]
    [Trait(EndToEndCollection.StageTrait, EndToEndCollection.AfterRepair)]
    public async Task Web_search_result_reaches_the_executor_prompt()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        host.Tavily.ResultText = $"{WebMarker}: The Nordkap lighthouse was first lit in 1911.";

        var runId = await host.SubmitAsync(new SubmitRunRequest(
            BriefingText: LighthouseBriefing,
            ConfigJson: "{}",
            CrewTemplateName: SystemCrew.KlassikTemplateName));
        await host.WaitForTerminalAsync(runId);

        Assert.True(
            host.Llm.AnyExecutorPromptContains(WebMarker),
            $"Web-search marker missing from every executor prompt (Tavily requests: {host.External.For(FakeTavilyEndpoint.ClientName).Count}).\n{host.Llm.Describe()}");
    }

    [Fact]
    [Trait(EndToEndCollection.StageTrait, EndToEndCollection.AfterRepair)]
    public async Task Classic_crew_delivers_web_search_and_attachment_side_by_side()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        host.Tavily.ResultText = $"{WebMarker}: The Nordkap lighthouse was first lit in 1911.";

        var runId = await host.SubmitAsync(new SubmitRunRequest(
            BriefingText: LighthouseBriefing,
            ConfigJson: "{}",
            CrewTemplateName: SystemCrew.KlassikTemplateName,
            Attachments: [LighthouseNotes()]));
        await host.WaitForTerminalAsync(runId);

        var missing = new[] { WebMarker, AttachmentMarker }
            .Where(marker => !host.Llm.AnyExecutorPromptContains(marker))
            .ToList();
        Assert.True(
            missing.Count == 0,
            $"Grounding providers were skipped; missing markers: {string.Join(", ", missing)}.\n{host.Llm.Describe()}");
    }

    [Fact]
    [Trait(EndToEndCollection.StageTrait, EndToEndCollection.SequenceDefect)]
    public async Task Two_tool_backed_grounding_providers_both_reach_the_executor_prompt()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        await host.WithScopeAsync(async sp =>
        {
            await SeedStaticContextProviderAsync(sp, "alpha", AlphaMarker);
            await SeedStaticContextProviderAsync(sp, "beta", BetaMarker);
            return true;
        });

        var runId = await host.SubmitAsync(new SubmitRunRequest(
            BriefingText: LighthouseBriefing,
            ConfigJson: "{}",
            CustomCrew: new CrewSpec(
                ExecutorProfileName: SystemCrew.DefaultExecutorProfile.Name,
                ReviewerProfileNames: [SystemCrew.BriefingFidelityProfile.Name],
                EvaluationStrategy: EvaluationStrategy.Parallel,
                ConvergenceOverride: null,
                GroundingProviderNames: ["custom-e2e-alpha", "custom-e2e-beta"])));
        await host.WaitForTerminalAsync(runId);

        var missing = new[] { AlphaMarker, BetaMarker }
            .Where(marker => !host.Llm.AnyExecutorPromptContains(marker))
            .ToList();
        Assert.True(
            missing.Count == 0,
            $"Tool-backed grounding providers were skipped; missing markers: {string.Join(", ", missing)}.\n{host.Llm.Describe()}");
    }

    private static RunAttachmentInput LighthouseNotes() => new(
        Filename: "nordkap-notes.txt",
        ContentType: "text/plain",
        Content: Encoding.UTF8.GetBytes(
            $"{AttachmentMarker}: The Nordkap lighthouse history notes. The tower was built in 1911 " +
            "from local granite and automated in 1987. The keeper's house now hosts a small museum."));

    private static async Task SeedStaticContextProviderAsync(IServiceProvider services, string suffix, string marker)
    {
        var toolName = $"e2e-context-{suffix}";
        await services.GetRequiredService<IToolDefinitionRepository>().UpsertAsync(new ToolDefinition(
            Name: toolName,
            DisplayName: $"E2E context {suffix}",
            Description: "Static context for the end-to-end test.",
            ToolType: ToolType.StaticContext,
            Settings: new Dictionary<string, string>
            {
                [ToolDefinitionSettingsKeys.StaticContent] = $"{marker}: background fact {suffix}.",
            },
            SecretRef: null,
            LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: false));

        await services.GetRequiredService<IGroundingProviderProfileRepository>().CreateAsync(new GroundingProviderProfile(
            Name: $"custom-e2e-{suffix}",
            DisplayName: $"E2E {suffix}",
            Description: "Tool-backed static context for the end-to-end test.",
            ProviderType: GroundingProviderTypes.StaticContext,
            ProviderSettings: new Dictionary<string, string>
            {
                [GroundingProviderProfile.KeyStaticContent] = $"{marker}: background fact {suffix}.",
                [GroundingProviderProfile.KeyStaticLabel] = $"E2E {suffix}",
            },
            MaxQueriesPerRun: 1,
            IsSystem: false,
            ToolName: toolName), CancellationToken.None);
    }
}
