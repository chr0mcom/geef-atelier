using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// D-087 over the real wiring (MCP, order service, intake, orchestrator, direct assembler, Postgres): with <c>Orders:CarefulOutlineOverrides</c>
/// on for one user, an "Extra careful" order of that user runs the outline template – one planning call, then the writing pass with the
/// outline, the report of the direct path and the cost line of the planning call. Orders of other users, and standard orders of the same
/// user, run exactly as before.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class CarefulOutlineEndToEndTests(PostgresFixture postgres)
{
    private const string Request = "Write a short notice for our team about the new opening hours of the canteen.";

    private const string Plan = """{"sections": [{"heading": "What changes", "words": 60, "content": "The new hours"}, {"heading": "What to do", "words": 40, "content": "Where to ask"}]}""";

    private static Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres, IntakeMode intake = IntakeMode.Off) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<OrderOptions>(o =>
            {
                o.IntakeMode = intake;
                o.CarefulOutlineOverrides[AtelierEndToEndHost.McpTokenUser] = CarefulOutlineMode.On;
            });
            services.PostConfigure<IntakeOptions>(o => o.PollInterval = TimeSpan.FromMilliseconds(200));
        });

    [Theory]
    [InlineData(IntakeMode.Off)]
    [InlineData(IntakeMode.On)]
    public async Task An_extra_careful_order_of_the_user_with_the_override_plans_first_and_then_writes_along_the_outline(IntakeMode intake)
    {
        await using var host = await StartAsync(postgres, intake);
        host.Script.EnqueueExecutorText(Plan);

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request, extraCareful = true });
        host.ReleaseOrchestrator();
        var orderId = Guid.Parse(submitted.GetProperty("orderId").GetString()!);
        if (submitted.GetProperty("state").GetString() == "needs_input")
            await host.CallMcpToolAsync("answer_order", new { orderId, skip = true });
        await host.WaitForOrderFinishedAsync(orderId, TimeSpan.FromSeconds(90));

        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId));
        Assert.True(run.Status == RunStatus.Completed, $"Status {run.Status}, error '{run.ErrorMessage}'.\n{host.Llm.Describe()}");
        Assert.Equal(SystemCrew.DirectOutlineTemplateName, run.CrewTemplateName);
        Assert.Equal(ScriptedLlmEndpoint.DefaultExecutorText, run.FinalText);
        Assert.False(string.IsNullOrWhiteSpace(run.ReportJson));

        var executor = host.Llm.CallsFor(LlmRole.Executor);
        Assert.Equal(2, executor.Count);
        Assert.Equal(WritingPlanPrompts.SystemPrompt, executor[0].SystemPrompt);
        Assert.Contains(WritingPlanPrompts.OutlineLead, executor[1].PromptText, StringComparison.Ordinal);
        Assert.Contains("1. What changes – about 60 words: The new hours", executor[1].PromptText, StringComparison.Ordinal);

        var actors = await host.QueryAsync(db => db.IterationActorCosts.AsNoTracking()
            .Where(c => db.Iterations.Any(i => i.Id == c.IterationId && i.RunId == run.Id))
            .Select(c => c.ActorName).ToListAsync());
        Assert.Contains(WritingPlanner.ActorName, actors);
        Assert.Contains(SystemCrew.DirectWriterProfile.Name, actors);

        var order = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));
        Assert.Equal(OrderMode.Careful, order.Mode);
        var result = await host.CallMcpToolAsync("get_order_result", new { orderId });
        Assert.Equal(ScriptedLlmEndpoint.DefaultExecutorText, result.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Other_users_and_standard_orders_of_the_same_user_resolve_as_before()
    {
        await using var host = await StartAsync(postgres);

        var web = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, "e2e-other-user", OrderEntry.Web, Mode: OrderMode.Careful));
        var standard = await host.CallMcpToolAsync("submit_order", new { request = Request });
        var standardId = Guid.Parse(standard.GetProperty("orderId").GetString()!);
        host.ReleaseOrchestrator();
        await host.WaitForOrderFinishedAsync(web.OrderId, TimeSpan.FromSeconds(90));
        await host.WaitForOrderFinishedAsync(standardId, TimeSpan.FromSeconds(90));

        var webRun = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == web.OrderId));
        var standardRun = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == standardId));
        Assert.Equal(SystemCrew.KlassikTemplateName, webRun.CrewTemplateName);
        Assert.Equal(SystemCrew.DirectTemplateName, standardRun.CrewTemplateName);
        Assert.DoesNotContain(host.Llm.CallsFor(LlmRole.Executor), c => c.SystemPrompt == WritingPlanPrompts.SystemPrompt);
    }
}
