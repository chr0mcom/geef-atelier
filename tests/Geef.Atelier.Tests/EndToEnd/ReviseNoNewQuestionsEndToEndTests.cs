using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Changing a delivered text asks nobody and looks nothing up, with the intake and the research switched on over the real wiring. Each
/// test first shows that version 1 really reaches the clarifier (and the search), so that the silence of version 2 means something.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ReviseNoNewQuestionsEndToEndTests(PostgresFixture postgres)
{
    private const string User = "e2e-revise-quiet";
    private const string Request = "Schreib einen Aushang zum neuen Mindestlohn für unser Lager-Team.";
    private const string Topic = "Mindestlohn 2027 Deutschland";
    private const string Passage = "Ab dem 1. Januar 2027 beträgt der gesetzliche Mindestlohn 14,60 Euro brutto je Stunde.";
    private const string PageText = "Mindestlohn 2027\n\nDie Kommission hat im Juni entschieden. " + Passage + "\nFür Auszubildende gelten eigene Regeln.";
    private const string PageUrl = "https://wages.example/mindestlohn-2027";
    private const string FirstText = "Liebes Lager-Team,\n\nab Januar gilt ein neuer Mindestlohn.\n\nViele Grüße";
    private const string SecondText = "Liebes Lager-Team,\n\nab dem 1. Januar gilt ein neuer Mindestlohn.\n\nViele Grüße";
    private const string Change = "Name the exact day it starts.";

    private const string Clarification =
        """{"text_type":"Aushang","language":"de","understanding":"Ein Aushang zum neuen Mindestlohn.","is_revision":false,"revision_file":null,"length_scale":"standard","gaps":[{"topic":"facts","resolution":"ask","user_can_know":true,"web_researchable":true,"impact":"high","assumption":"Betrag als Platzhalter","confidence":"unsure","question":{"text":"Wie hoch ist der neue Mindestlohn?","options":["12,82 Euro","Anderer Betrag"]}}],"research_topics":["Mindestlohn 2027 Deutschland"]}""";

    private static string UserPrompt(RecordedLlmCall call) => call.Messages.Last(m => m.Role == "user").Content ?? string.Empty;

    private static Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres, ResearchMode research) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<OrderOptions>(o =>
            {
                o.IntakeMode = IntakeMode.On;
                o.Revise = ReviseMode.On;
            });
            services.PostConfigure<IntakeOptions>(o => o.PollInterval = TimeSpan.FromMilliseconds(200));
            services.PostConfigure<ResearchOptions>(o => o.Mode = research);
        });

    private static async Task<Guid> FirstVersionAsync(AtelierEndToEndHost host)
    {
        host.Script.EnqueueClarifierJson(Clarification);
        host.Script.EnqueueExecutorText(FirstText);
        host.Tavily.EnqueueSearch(new FakeSearchHit(PageUrl, "Mindestlohn 2027", PageText));
        host.Script.PassagesResponder = call =>
        {
            var prompt = UserPrompt(call);
            var start = prompt.IndexOf("<page id=\"", StringComparison.Ordinal) + "<page id=\"".Length;
            var page = prompt[start..prompt.IndexOf('"', start)];
            return JsonSerializer.Serialize(new { passages = new[] { new { topic = 1, page, quote = Passage } } });
        };
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, User, OrderEntry.Web));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        return submission.OrderId;
    }

    private static async Task<RunEntity> ReviseAsync(AtelierEndToEndHost host, Guid orderId)
    {
        host.Script.EnqueueExecutorText(SecondText);
        var revision = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>()
            .ReviseAsync(new ReviseOrderRequest(orderId, User, Change, [], OrderEntry.Web)));
        Assert.Equal(2, revision.Version);
        await host.WaitForOrderFinishedAsync(orderId);
        await Task.Delay(TimeSpan.FromSeconds(2));
        return await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.OrderVersion == 2));
    }

    private static int ClarifierCalls(AtelierEndToEndHost host) => host.Llm.Calls.Count(ScriptedLlmEndpoint.IsClarifierCall);

    private static int PassageCalls(AtelierEndToEndHost host) => host.Llm.Calls.Count(ScriptedLlmEndpoint.IsPassagesCall);

    private static int Searches(AtelierEndToEndHost host) => host.External.For(FakeTavilyEndpoint.ClientName).Count();

    private static Task<string?> ResearchJsonAsync(AtelierEndToEndHost host, Guid orderId) =>
        host.QueryAsync(db => db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.ResearchJson).SingleAsync());

    private static void WriterGotTheDeliveredTextAndTheChange(RecordedLlmCall writer)
    {
        Assert.Equal(FirstText, writer.Document);
        Assert.Contains(DirectWriterPrompts.RevisionInstruction, UserPrompt(writer), StringComparison.Ordinal);
        Assert.Contains(Change, UserPrompt(writer), StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_intake_and_research_on_a_change_asks_no_clarifier_and_searches_nothing_new()
    {
        await using var host = await StartAsync(postgres, ResearchMode.On);
        var orderId = await FirstVersionAsync(host);
        Assert.Equal((1, 1, 1), (ClarifierCalls(host), Searches(host), PassageCalls(host)));
        var stored = await ResearchJsonAsync(host, orderId);
        Assert.Equal(ResearchStatus.Found, ResearchJson.TryParse(stored)!.Status);
        var writersBefore = host.Llm.CallsFor(LlmRole.Executor).Count;

        var second = await ReviseAsync(host, orderId);

        Assert.Equal((RunStatus.Completed, SecondText), (second.Status, second.FinalText));
        Assert.Equal((1, 1, 1), (ClarifierCalls(host), Searches(host), PassageCalls(host)));
        Assert.Equal(stored, await ResearchJsonAsync(host, orderId));
        Assert.Empty(await host.QueryAsync(db => db.OrderIntakeShadows.AsNoTracking().ToListAsync()));
        var writer = Assert.Single(host.Llm.CallsFor(LlmRole.Executor).Skip(writersBefore));
        WriterGotTheDeliveredTextAndTheChange(writer);
        Assert.Contains("[W1.1] " + Passage, (writer.ContextDocument ?? string.Empty) + UserPrompt(writer), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_text_written_before_the_research_was_switched_on_is_changed_without_a_first_search()
    {
        await using var host = await StartAsync(postgres, ResearchMode.Off);
        var orderId = await FirstVersionAsync(host);
        Assert.Equal((1, 0, 0), (ClarifierCalls(host), Searches(host), PassageCalls(host)));
        Assert.Null(await ResearchJsonAsync(host, orderId));
        host.Services.GetRequiredService<IOptions<ResearchOptions>>().Value.Mode = ResearchMode.On;
        var writersBefore = host.Llm.CallsFor(LlmRole.Executor).Count;

        var second = await ReviseAsync(host, orderId);

        Assert.Equal((RunStatus.Completed, SecondText), (second.Status, second.FinalText));
        Assert.Equal((1, 0, 0), (ClarifierCalls(host), Searches(host), PassageCalls(host)));
        Assert.Null(await ResearchJsonAsync(host, orderId));
        WriterGotTheDeliveredTextAndTheChange(Assert.Single(host.Llm.CallsFor(LlmRole.Executor).Skip(writersBefore)));

        host.Script.EnqueueClarifierJson(Clarification);
        host.Tavily.EnqueueSearch(new FakeSearchHit(PageUrl, "Mindestlohn 2027", PageText));
        var control = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, User, OrderEntry.Web));
        await host.WaitForOrderFinishedAsync(control.OrderId);
        Assert.Equal((2, 1), (ClarifierCalls(host), Searches(host)));
    }
}
