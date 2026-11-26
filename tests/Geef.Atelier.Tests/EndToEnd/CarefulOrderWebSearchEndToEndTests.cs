using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence.Profiles;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// D-079 over the real wiring: an order with the default crew ("Extra careful" over MCP and on the web, <c>submit_request</c> without
/// a template) never reaches the search service, also not when a run of it is resumed with its stored snapshot that still names the
/// web search; the About me details still reach the writer. A template chosen on purpose keeps its web search, and that search fails
/// closed when no query can be formed.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class CarefulOrderWebSearchEndToEndTests(PostgresFixture postgres)
{
    private const string ProfileMarker = "PROFILMARKER Lindenallee 4 privat";
    private const string Request = "Write a short notice for our team about the new opening hours of the canteen.";

    [Fact]
    public async Task An_extra_careful_order_with_about_me_details_sends_nothing_to_the_search_service()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        await SaveProfileAsync(host, AtelierEndToEndHost.McpTokenUser);

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request, extraCareful = true });
        host.ReleaseOrchestrator();
        var orderId = Guid.Parse(submitted.GetProperty("orderId").GetString()!);
        await host.WaitForOrderFinishedAsync(orderId);

        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId));
        Assert.True(run.Status == RunStatus.Completed, $"Status {run.Status}, error '{run.ErrorMessage}'.\n{host.Llm.Describe()}");
        Assert.Equal(SystemCrew.KlassikTemplateName, run.CrewTemplateName);
        Assert.True(host.Llm.AnyExecutorPromptContains(ProfileMarker), host.Llm.Describe());
        AssertNothingSearched(host);
    }

    [Fact]
    public async Task A_careful_web_order_and_submit_request_without_a_template_send_nothing_to_the_search_service()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
            services.PostConfigure<OrderOptions>(o => o.LegacySubmitDefaultMode = OrderMode.Careful));
        const string webUser = "e2e-careful-web";
        await SaveProfileAsync(host, webUser);
        await SaveProfileAsync(host, AtelierEndToEndHost.McpTokenUser);

        var web = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, webUser, OrderEntry.Web, Mode: OrderMode.Careful));
        var legacy = await host.CallMcpToolAsync("submit_request", new { briefingText = Request });
        var legacyRun = await host.WaitForTerminalAsync(Guid.Parse(legacy.GetProperty("runId").GetString()!));
        await host.WaitForOrderFinishedAsync(web.OrderId);

        var webRun = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == web.OrderId));
        Assert.All(new[] { webRun, legacyRun }, r => Assert.Equal(SystemCrew.KlassikTemplateName, r.CrewTemplateName));
        Assert.All(new[] { webRun, legacyRun }, r => Assert.Equal(RunStatus.Completed, r.Status));
        Assert.True(host.Llm.ExecutorPrompts().Count(p => p.Contains(ProfileMarker, StringComparison.Ordinal)) >= 2, host.Llm.Describe());
        AssertNothingSearched(host);
    }

    [Fact]
    public async Task A_resumed_run_of_a_careful_order_with_its_stored_snapshot_sends_nothing_to_the_search_service()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        await SaveProfileAsync(host, AtelierEndToEndHost.McpTokenUser);

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request, extraCareful = true });
        host.ReleaseOrchestrator();
        var orderId = Guid.Parse(submitted.GetProperty("orderId").GetString()!);
        await host.WaitForOrderFinishedAsync(orderId);
        var first = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId));
        Assert.Contains($"\"{SystemCrew.TavilyRefinedProfile.Name}\"", first.CrewSnapshot);

        await host.QueryAsync(db => db.Runs.Where(r => r.Id == first.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Failed).SetProperty(r => r.Outcome, RunOutcome.Failed)));
        var resumedId = await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>()
            .ResumeRunAsync(new ResumeOptions(first.Id, UseSeedDraft: true, MaxIterationsOverride: null), AtelierEndToEndHost.McpTokenUser));
        var resumed = await host.WaitForTerminalAsync(resumedId);

        Assert.Equal(orderId, resumed.OrderId);
        Assert.Contains($"\"{SystemCrew.TavilyRefinedProfile.Name}\"", resumed.CrewSnapshot);
        Assert.Equal(RunStatus.Completed, resumed.Status);
        AssertNothingSearched(host);
    }

    [Fact]
    public async Task A_template_chosen_on_purpose_still_searches_with_the_formed_query_only()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        await SaveProfileAsync(host, AtelierEndToEndHost.McpTokenUser);

        var submitted = await host.CallMcpToolAsync("submit_request", new { briefingText = Request, crewTemplate = SystemCrew.KlassikTemplateName });
        host.ReleaseOrchestrator();
        await host.WaitForTerminalAsync(Guid.Parse(submitted.GetProperty("runId").GetString()!));

        var search = Assert.Single(host.External.For(FakeTavilyEndpoint.ClientName));
        Assert.Contains("e2e lighthouse history search", search.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("canteen", search.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ProfileMarker, search.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_template_chosen_on_purpose_searches_nothing_when_no_query_can_be_formed()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        host.Script.QueryExtractorText = "   ";

        var submitted = await host.CallMcpToolAsync("submit_request", new { briefingText = Request, crewTemplate = SystemCrew.KlassikTemplateName });
        host.ReleaseOrchestrator();
        var runId = Guid.Parse(submitted.GetProperty("runId").GetString()!);
        var run = await host.WaitForTerminalAsync(runId);

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.NotEmpty(host.Llm.CallsFor(LlmRole.QueryExtractor));
        Assert.Empty(host.External.For(FakeTavilyEndpoint.ClientName));
        var warnings = await host.QueryAsync(db => db.RunWarnings.AsNoTracking().Where(w => w.RunId == runId).ToListAsync());
        var skipped = Assert.Single(warnings, w => w.Code == RunWarningCodes.GroundingProviderSkipped);
        Assert.Contains($"\"{SystemCrew.TavilyRefinedProfile.DisplayName}\"", skipped.Message);
    }

    private static void AssertNothingSearched(AtelierEndToEndHost host)
    {
        Assert.Empty(host.External.For(FakeTavilyEndpoint.ClientName));
        Assert.Empty(host.Tavily.RequestBodies);
        Assert.DoesNotContain(host.External.All, c => c.Client.Contains("academic-", StringComparison.Ordinal));
        Assert.Empty(host.Llm.CallsFor(LlmRole.QueryExtractor));
        Assert.Empty(host.Llm.CallsFor(LlmRole.Refiner));
    }

    private static Task<bool> SaveProfileAsync(AtelierEndToEndHost host, string user) =>
        host.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IUserProfileRepository>().UpsertAsync(new UserProfileEntity
            {
                Username = user,
                DisplayName = "Kim Beispiel",
                Formality = Formality.Formal,
                FreeText = ProfileMarker,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            return true;
        });
}
