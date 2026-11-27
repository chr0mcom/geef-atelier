using System.Text.Json.Nodes;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Runs and orders that older versions stored (stage 8d, D-091): the old review loop with its snapshot, a composed team and a learning
/// run. The database keeps them, so the start, the lists, "My work", the pages and the orchestrator with its crash recovery must cope:
/// finished ones stay readable, pending ones fail with a plain sentence instead of running as something else. Orders in the mode
/// "careful" and runs of the removed template <c>direct-outline</c> (addendum to D-087) stay readable, exportable and changeable, and
/// whatever of them is still to be done runs on the direct path.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class LegacyDataEndToEndTests(PostgresFixture postgres)
{
    private const string OldText = "# Old text\n\nWritten by the removed review loop.";
    private const string FinishedText = "Dear all, our new opening hours are from eight to five. Kind regards";
    private const string ChangedText = "Dear all, from Monday our new opening hours are from eight to five. Kind regards";

    [Fact(Timeout = 90_000)]
    public async Task Finished_old_orders_stay_readable_in_lists_my_work_and_on_their_pages()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var (careful, carefulRun) = await StoreFinishedOldOrderAsync(host, user, OrderMode.Careful, RunKind.Standard);
        var (composed, composedRun) = await StoreComposedOldOrderAsync(host, user);

        await using var scope = host.Services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var listed = await orders.ListAsync(user);
        var result = await orders.GetResultAsync(careful, user);
        var view = await orders.GetAsync(composed, user);
        var runView = await scope.ServiceProvider.GetRequiredService<IRunService>().GetRunViewAsync(carefulRun, user);

        Assert.Contains(listed, o => o.OrderId == careful);
        Assert.Contains(listed, o => o.OrderId == composed);
        Assert.Contains("Written by the removed review loop.", result!.Text);
        Assert.NotNull(view);
        Assert.Equal("klassik", runView!.Snapshot!.TemplateName);

        using var client = await host.CreateSignedInClientAsync(user);
        foreach (var path in new[] { "/work", "/orders", "/runs", $"/orders/{careful}", $"/orders/{composed}", $"/runs/{carefulRun}", $"/runs/{composedRun}" })
        {
            using var response = await client.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode, $"{path}: {(int)response.StatusCode}");
        }
        var runPage = await client.GetStringAsync($"/runs/{carefulRun}");
        Assert.Contains("Setup: Classic", runPage, StringComparison.Ordinal);
    }

    [Fact(Timeout = 90_000)]
    public async Task Pending_old_runs_fail_with_a_plain_sentence_and_running_ones_are_recovered()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var oldLoop = await StorePendingOldRunAsync(host, user, RunKind.Standard, withOrder: true);
        var composition = await StorePendingOldRunAsync(host, user, RunKind.CrewComposition, withOrder: true);
        var learning = await StorePendingOldRunAsync(host, user, RunKind.Learning, withOrder: false);
        var interrupted = await StoreRunningOldRunAsync(host, user);

        host.ReleaseOrchestrator();

        foreach (var runId in new[] { oldLoop, composition, learning })
        {
            var run = await host.WaitForTerminalAsync(runId, TimeSpan.FromSeconds(30));
            Assert.Equal(RunStatus.Failed, run.Status);
            Assert.Equal((RunOutcome?)RunOutcome.Failed, run.Outcome);
            Assert.Equal(PipelineAssemblerSelector.RetiredSetupMessage, run.ErrorMessage);
        }

        var recovered = await host.WaitForTerminalAsync(interrupted, TimeSpan.FromSeconds(30));
        Assert.Equal(RunStatus.Failed, recovered.Status);
        Assert.Equal((RunOutcome?)RunOutcome.Failed, recovered.Outcome);

        await using var scope = host.Services.CreateAsyncScope();
        Assert.NotEmpty(await scope.ServiceProvider.GetRequiredService<IOrderService>().ListAsync(user));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_finished_careful_order_of_the_outline_template_is_read_exported_and_changed_on_the_direct_path()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, s => s.PostConfigure<OrderOptions>(o => o.Revise = ReviseMode.On));
        var user = AtelierEndToEndHost.McpTokenUser;
        host.Script.ExecutorText = FinishedText;
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest("Write a short note about the new opening hours.", user, OrderEntry.Web));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        var runId = submission.RunId!.Value;
        await StoreAsOutlineRunOfACarefulOrderAsync(host, submission.OrderId, runId);

        var orders = (await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().ListAsync(user))).Select(o => o.OrderId);
        var view = (await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(submission.OrderId, user)))!;
        var result = (await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(submission.OrderId, user)))!;

        Assert.Contains(submission.OrderId, orders);
        Assert.Equal(OrderDisplayState.Delivered, view.Display.State);
        Assert.Equal(["write", "check", "done"], view.Progress.Steps.Select(s => s.Key));
        Assert.All(view.Progress.Steps, s => Assert.Equal(OrderStepStatus.Done, s.Status));
        Assert.Equal(FinishedText, result.Text);

        using var client = await host.CreateSignedInClientAsync(user);
        foreach (var path in new[] { "/work", $"/orders/{submission.OrderId}", $"/runs/{runId}" })
        {
            var page = await client.GetStringAsync(path);
            Assert.DoesNotContain("data-testid=\"order-mode-careful\"", page, StringComparison.Ordinal);
        }
        Assert.Contains("Setup: Extra careful", await client.GetStringAsync($"/runs/{runId}"), StringComparison.Ordinal);
        using var export = await client.GetAsync($"/orders/{submission.OrderId}/export/md");
        Assert.True(export.IsSuccessStatusCode, $"{(int)export.StatusCode}");
        Assert.Contains(FinishedText, await export.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var mcpExport = await host.CallMcpToolAsync("export_order", new { orderId = submission.OrderId.ToString(), format = "md" });
        Assert.Contains("opening hours", mcpExport.GetRawText(), StringComparison.Ordinal);

        host.Script.ExecutorText = ChangedText;
        var revised = await host.CallMcpToolAsync("revise_order", new { orderId = submission.OrderId.ToString(), change = "Say that it starts on Monday." });
        Assert.Equal(2, revised.GetProperty("version").GetInt32());
        await host.WaitForOrderFinishedAsync(submission.OrderId);

        var changed = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == submission.OrderId && r.OrderVersion == 2));
        Assert.True(changed.Status == RunStatus.Completed, $"{changed.Status}: {changed.ErrorMessage}\n{host.Llm.Describe()}");
        Assert.Equal((SystemCrew.DirectTemplateName, ChangedText, (int?)2), (changed.CrewTemplateName, changed.FinalText, changed.OrderVersion));
        Assert.Equal(SystemCrew.DirectTemplateName, CrewSnapshot.Deserialize(changed.CrewSnapshot)!.TemplateName);
        var after = (await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(submission.OrderId, user)))!;
        Assert.Equal(["write", "check", "done"], after.Progress.Steps.Select(s => s.Key));
        Assert.Equal(OrderMode.Careful, await host.QueryAsync(db => db.Orders.Where(o => o.Id == submission.OrderId).Select(o => o.Mode).SingleAsync()));
    }

    [Fact(Timeout = 90_000)]
    public async Task A_pending_run_of_the_outline_template_runs_on_the_direct_path_without_its_removed_steps()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        host.Script.ExecutorText = FinishedText;
        var submission = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>()
            .SubmitAsync(new SubmitOrderRequest("Write a short note about the new opening hours.", user, OrderEntry.Web)));
        await StoreAsOutlineRunOfACarefulOrderAsync(host, submission.OrderId, submission.RunId!.Value);

        host.ReleaseOrchestrator();
        var run = await host.WaitForTerminalAsync(submission.RunId.Value);

        Assert.True(run.Status == RunStatus.Completed, $"{run.Status}: {run.ErrorMessage}\n{host.Llm.Describe()}");
        Assert.Equal(FinishedText, run.FinalText);
        Assert.NotNull(run.ReportJson);
        Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        Assert.Single(host.Llm.Calls);
        Assert.Empty(await host.QueryAsync(db => db.RunWarnings.AsNoTracking().Where(w => w.RunId == run.Id).ToListAsync()));
    }

    [Fact(Timeout = 90_000)]
    public async Task A_failed_run_of_the_outline_template_can_be_continued_on_the_direct_path()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        host.Script.ExecutorText = FinishedText;
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest("Write a short note about the new opening hours.", user, OrderEntry.Web));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        var runId = submission.RunId!.Value;
        await StoreAsOutlineRunOfACarefulOrderAsync(host, submission.OrderId, runId);
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RunStatus.Failed).SetProperty(r => r.Outcome, RunOutcome.Failed).SetProperty(r => r.FinalText, (string?)null)));

        var resumedId = await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>()
            .ResumeRunAsync(new ResumeOptions(runId, UseSeedDraft: false, MaxIterationsOverride: null), user));
        var resumed = await host.WaitForTerminalAsync(resumedId);

        Assert.True(resumed.Status == RunStatus.Completed, $"{resumed.Status}: {resumed.ErrorMessage}\n{host.Llm.Describe()}");
        Assert.Equal((submission.OrderId, FinishedText), (resumed.OrderId!.Value, resumed.FinalText));
    }

    [Fact(Timeout = 90_000)]
    public async Task A_stored_careful_order_that_waits_for_answers_runs_on_the_direct_path()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, s => s.PostConfigure<OrderOptions>(o => o.IntakeMode = IntakeMode.On));
        var user = NewUser();
        var record = IntakeRecord.Fallback(IntakeRecordStatus.Ok, null, 10, null, BriefMode.AppendAnswers) with
        {
            Output = new ClarifierOutput("Note", "en", "A short note.", false, null, LengthScale.Standard, [], []),
            Questions = [QuestionTemplates.Length("en", LengthScale.Standard)],
        };
        var now = DateTimeOffset.UtcNow;
        var order = Order(user, "Write a short note about the new opening hours.") with
        {
            Mode = OrderMode.Careful, State = OrderState.NeedsInput, ClarifyJson = IntakeRecordJson.Serialize(record), QuestionsAskedAt = now,
        };
        await host.QueryAsync(async db =>
        {
            db.Orders.Add(order);
            return await db.SaveChangesAsync();
        });
        var waiting = (await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(order.Id, user)))!;
        Assert.Equal(OrderDisplayState.NeedsInput, waiting.Display.State);
        Assert.Equal(["understand", "answers", "write", "check", "done"], waiting.Progress.Steps.Select(s => s.Key));
        host.Script.ExecutorText = FinishedText;

        var answered = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SkipClarificationAsync(order.Id, user));
        host.ReleaseOrchestrator();
        var run = await host.WaitForTerminalAsync(answered.RunId!.Value);

        Assert.True(run.Status == RunStatus.Completed, $"{run.Status}: {run.ErrorMessage}\n{host.Llm.Describe()}");
        Assert.Equal(SystemCrew.DirectTemplateName, run.CrewTemplateName);
        Assert.Equal([SystemCrew.DeterministicChecksProfile.Name, SystemCrew.SourceFactCheckProfile.Name],
            CrewSnapshot.Deserialize(run.CrewSnapshot)!.Reviewers.Select(r => r.Name));
        Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
    }

    [Fact(Timeout = 90_000)]
    public async Task A_client_with_an_older_tool_list_that_still_sends_extraCareful_gets_the_direct_path()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        host.Script.ExecutorText = FinishedText;

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = "Write a short note about the new opening hours.", extraCareful = true });
        host.ReleaseOrchestrator();

        var orderId = Guid.Parse(submitted.GetProperty("orderId").GetString()!);
        await host.WaitForOrderFinishedAsync(orderId);
        var order = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId));
        Assert.Equal((OrderMode.Direct, SystemCrew.DirectTemplateName, RunStatus.Completed), (order.Mode, run.CrewTemplateName, run.Status));
    }

    /// <summary>
    /// Rewrites an order and one of its runs into what the removed variant "Extra careful" stored: mode careful, template
    /// <c>direct-outline</c> and a snapshot with the third check <c>requirements-check</c>.
    /// </summary>
    private static async Task StoreAsOutlineRunOfACarefulOrderAsync(AtelierEndToEndHost host, Guid orderId, Guid runId)
    {
        var stored = await host.QueryAsync(db => db.Runs.AsNoTracking().Where(r => r.Id == runId).Select(r => r.CrewSnapshot).SingleAsync());
        var snapshot = JsonNode.Parse(stored!)!.AsObject();
        snapshot["templateName"] = SystemCrew.RetiredOutlineTemplateName;
        var reviewers = snapshot["reviewers"]!.AsArray();
        var check = reviewers[^1]!.DeepClone().AsObject();
        check["name"] = SystemCrew.RetiredRequestCheckName;
        check["displayName"] = "Request check";
        check["systemPrompt"] = "Requirements check -- injected at pipeline build time.";
        check["modelSlot"] = null;
        reviewers.Add(check);
        var json = snapshot.ToJsonString();

        await host.QueryAsync(db => db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.CrewTemplateName, SystemCrew.RetiredOutlineTemplateName).SetProperty(r => r.CrewSnapshot, json)));
        await host.QueryAsync(db => db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Mode, OrderMode.Careful)));
    }

    private static async Task<(Guid Order, Guid Run)> StoreFinishedOldOrderAsync(AtelierEndToEndHost host, string user, OrderMode mode, RunKind kind)
    {
        var order = Order(user, "Write the old text.") with { Mode = mode, CrewTemplateName = "klassik" };
        var run = OldRun(user, order.Id, kind, RunStatus.Completed, RunOutcome.Converged, OldText);
        await host.QueryAsync(async db =>
        {
            db.Orders.Add(order);
            db.Runs.Add(run);
            return await db.SaveChangesAsync();
        });
        return (order.Id, run.Id);
    }

    private static async Task<(Guid Order, Guid Run)> StoreComposedOldOrderAsync(AtelierEndToEndHost host, string user)
    {
        var order = Order(user, "Write the composed text.") with { Mode = OrderMode.Careful };
        var composition = OldRun(user, order.Id, RunKind.CrewComposition, RunStatus.Completed, RunOutcome.Converged, "{\"team\":\"old\"}");
        var standard = OldRun(user, order.Id, RunKind.Standard, RunStatus.Completed, RunOutcome.DeliveredWithOpenPoints, OldText,
            parentComposition: composition.Id, at: composition.CreatedAt.AddSeconds(1));
        await host.QueryAsync(async db =>
        {
            db.Orders.Add(order);
            db.Runs.AddRange(composition, standard);
            return await db.SaveChangesAsync();
        });
        return (order.Id, standard.Id);
    }

    private static async Task<Guid> StorePendingOldRunAsync(AtelierEndToEndHost host, string user, RunKind kind, bool withOrder)
    {
        var order = withOrder ? Order(user, "Write a text with an old setup.") with { Mode = OrderMode.Careful, CrewTemplateName = "klassik" } : null;
        var run = OldRun(user, order?.Id, kind, RunStatus.Pending, null, null);
        await host.QueryAsync(async db =>
        {
            if (order is not null)
                db.Orders.Add(order);
            db.Runs.Add(run);
            return await db.SaveChangesAsync();
        });
        return run.Id;
    }

    private static async Task<Guid> StoreRunningOldRunAsync(AtelierEndToEndHost host, string user)
    {
        var order = Order(user, "Write a text that was interrupted.") with { Mode = OrderMode.Careful, CrewTemplateName = "klassik" };
        var run = OldRun(user, order.Id, RunKind.Standard, RunStatus.Running, null, null, at: DateTimeOffset.UtcNow.AddMinutes(-5));
        await host.QueryAsync(async db =>
        {
            db.Orders.Add(order);
            db.Runs.Add(run);
            return await db.SaveChangesAsync();
        });
        return run.Id;
    }

    private static RunEntity OldRun(string user, Guid? orderId, RunKind kind, RunStatus status, RunOutcome? outcome, string? text,
        Guid? parentComposition = null, DateTimeOffset? at = null)
    {
        var created = at ?? DateTimeOffset.UtcNow;
        var finished = status is RunStatus.Completed or RunStatus.Failed;
        return new RunEntity
        {
            Id = Guid.NewGuid(), CreatedAt = created, Status = status, BriefingText = "Write a text with an old setup.", ConfigJson = "{}",
            CreatedByUser = user, Kind = kind, OrderId = orderId, OrderVersion = orderId is null ? null : 1,
            CrewTemplateName = kind == RunKind.Standard ? "klassik" : null, CrewSnapshot = TestCrewSnapshots.RetiredKlassikJson,
            StartedAt = status == RunStatus.Pending ? null : created, CompletedAt = finished ? created.AddMinutes(1) : null,
            FinalText = text, WordCount = text is null ? null : 8, Outcome = outcome, DeliveredIteration = outcome is null ? null : 1,
            ParentCompositionRunId = parentComposition,
        };
    }
}
