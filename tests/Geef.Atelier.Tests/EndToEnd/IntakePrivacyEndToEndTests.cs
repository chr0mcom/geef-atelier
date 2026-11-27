using System.Text.Json;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Profiles;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Over the real wiring: the "About me" profile reaches the clarifier and every writer briefing (answered, skipped,
/// started after the deadline), but never an outside search service. A clarified order without research topics searches nothing at all
/// (D-079).
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class IntakePrivacyEndToEndTests(PostgresFixture postgres)
{
    private const string ProfileMarker = "PROFILMARKER Kiefernweg 17 privat";
    private const string AnswerMarker = "ANTWORTMARKER Innenhof Nord";
    private const string AssumptionMarker = "ANNAHMEMARKER Datum bleibt offen";
    private const string Request = "Schreib eine kurze Pressemitteilung über die Eröffnung unseres neuen Standorts in Leipzig.";

    private const string AskLength =
        """{"topic":"length","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Etwa 300 Wörter","confidence":"unsure","question":null}""";

    private static readonly string AssumeDate =
        $$"""{"topic":"date","resolution":"assume","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"{{AssumptionMarker}}","confidence":"unsure","question":null}""";

    private const string AskFormality =
        """{"topic":"formality","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Per Sie","confidence":"unsure","question":{"text":"Sollen die Leser geduzt oder gesiezt werden?","options":["Geduzt","Gesiezt"],"default_index":null,"allow_free_text":false}}""";

    private static string Clarify(string gaps) =>
        $$"""{"text_type":"Pressemitteilung","language":"de","understanding":"Eine Pressemitteilung zur Eröffnung.","is_revision":false,"revision_file":null,"length_scale":"standard","gaps":[{{gaps}}],"research_topics":[]}""";

    [Fact]
    public async Task A_clarified_order_gives_profile_answers_and_assumptions_to_the_writer_and_nothing_to_search()
    {
        await using var host = await StartAsync(IntakeMode.On);
        await SaveProfileAsync(host, AtelierEndToEndHost.McpTokenUser);
        host.Tavily.ResultText = "Leipzig hat einen neuen Standort.";
        host.Script.EnqueueClarifierJson(Clarify($"{AskLength},{AssumeDate}"));

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request });
        host.ReleaseOrchestrator();
        Assert.Equal("needs_input", submitted.GetProperty("state").GetString());
        var clarifier = Assert.Single(host.Llm.Calls, ScriptedLlmEndpoint.IsClarifierCall);
        Assert.Contains(ProfileMarker, clarifier.RawBody, StringComparison.Ordinal);

        var orderId = submitted.GetProperty("orderId").GetString()!;
        var answered = await host.CallMcpToolAsync("answer_order", new
        {
            orderId,
            answers = new object[] { new { questionId = "length", freeText = AnswerMarker, origin = "user" } },
        });
        var runId = Guid.Parse(answered.GetProperty("runId").GetString()!);
        await host.WaitForTerminalAsync(runId);

        foreach (var marker in new[] { ProfileMarker, AnswerMarker, AssumptionMarker })
            Assert.True(host.Llm.AnyExecutorPromptContains(marker), $"{marker} missing for the writer.\n{host.Llm.Describe()}");
        Assert.Empty(host.External.For(FakeTavilyEndpoint.ClientName));
        foreach (var marker in new[] { ProfileMarker, AnswerMarker, AssumptionMarker })
            AssertResearchSawOnlyTheRequest(host, marker);
    }

    [Fact]
    public async Task The_clarifier_gets_the_profile_and_does_not_ask_what_the_profile_answers()
    {
        await using var host = await StartAsync(IntakeMode.On);
        await SaveProfileAsync(host, AtelierEndToEndHost.McpTokenUser);
        host.Script.EnqueueClarifierJson(Clarify(AskFormality));

        var submitted = await host.CallMcpToolAsync("submit_order", new { request = Request + " Etwa 300 Wörter." });

        var clarifier = Assert.Single(host.Llm.Calls, ScriptedLlmEndpoint.IsClarifierCall);
        Assert.Contains(ProfileMarker, clarifier.RawBody, StringComparison.Ordinal);
        Assert.Contains(submitted.GetProperty("state").GetString(), new[] { "queued", "working" });
        Assert.Equal(0, submitted.TryGetProperty("questions", out var questions) && questions.ValueKind == JsonValueKind.Array ? questions.GetArrayLength() : 0);
    }

    [Fact]
    public async Task Answered_skipped_and_auto_started_orders_give_the_profile_to_the_writer()
    {
        await using var host = await StartAsync(IntakeMode.On, withWorker: false);
        var user = AtelierEndToEndHost.McpTokenUser;
        await SaveProfileAsync(host, user);
        var record = IntakeRecord.Fallback(IntakeRecordStatus.Ok, null, 10, null, BriefMode.AppendAnswers) with
        {
            Output = new ClarifierOutput("Pressemitteilung", "de", "Eine Pressemitteilung.", false, null, LengthScale.Standard, [], []),
            Assumptions = [new IntakeAssumption("a1", GapTopic.Date, AssumptionMarker, AssumptionConfidence.Unsure)],
            Questions = [QuestionTemplates.Length("de", LengthScale.Standard)],
        };

        var answered = await WaitingAsync(host, user, record);
        var skipped = await WaitingAsync(host, user, record);
        var late = await WaitingAsync(host, user, record);
        await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().AnswerAsync(
            new AnswerOrderRequest(answered, user, [new OrderAnswer("length", "short", null, false, AnswerOrigin.User)])));
        await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SkipClarificationAsync(skipped, user));
        var autoStarted = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderIntakeService>()
            .AutoStartUnansweredAsync(DateTimeOffset.UtcNow + TimeSpan.FromDays(2), CancellationToken.None));
        Assert.Equal(1, autoStarted);
        host.ReleaseOrchestrator();

        foreach (var (orderId, outcome) in new[] { (answered, IntakeOutcome.Answered), (skipped, IntakeOutcome.Skipped), (late, IntakeOutcome.AutoStarted) })
        {
            var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId));
            Assert.Equal(outcome, (await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId))).IntakeOutcome);
            Assert.Contains(ProfileMarker, run.BriefingText, StringComparison.Ordinal);
            Assert.Contains(AssumptionMarker, run.BriefingText, StringComparison.Ordinal);
            await host.WaitForTerminalAsync(run.Id);
        }

        var writerPrompts = host.Llm.CallsFor(LlmRole.Executor).Select(c => c.Messages.Last(m => m.Role == "user").Content ?? string.Empty).ToList();
        Assert.True(writerPrompts.Count(p => p.Contains(ProfileMarker, StringComparison.Ordinal)) >= 3, host.Llm.Describe());
    }

    private static void AssertResearchSawOnlyTheRequest(AtelierEndToEndHost host, string marker)
    {
        var leaks = host.External.All
            .Where(c => c.Url.Contains(marker, StringComparison.Ordinal) || Uri.UnescapeDataString(c.Url).Contains(marker, StringComparison.Ordinal)
                        || c.Body.Contains(marker, StringComparison.Ordinal))
            .Select(c => $"{c.Client} {c.Url}")
            .ToList();
        Assert.True(leaks.Count == 0, $"'{marker}' reached an outside service: {string.Join("; ", leaks)}");

        var shaping = host.Llm.Calls.Where(c => c.Role is LlmRole.QueryExtractor or LlmRole.Refiner).ToList();
        Assert.DoesNotContain(shaping, c => c.RawBody.Contains(marker, StringComparison.Ordinal));
        Assert.DoesNotContain(host.External.All, c => c.Body.Contains("Kiefernweg", StringComparison.Ordinal));
    }

    private static async Task<Guid> WaitingAsync(AtelierEndToEndHost host, string user, IntakeRecord record)
    {
        var submission = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SubmitAsync(new SubmitOrderRequest(Request, user, OrderEntry.Web)));
        Assert.True(await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderRepository>()
            .TrySaveQuestionsAsync(submission.OrderId, IntakeRecordJson.Serialize(record), DateTimeOffset.UtcNow)));
        return submission.OrderId;
    }

    private static Task<bool> SaveProfileAsync(AtelierEndToEndHost host, string user) =>
        host.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IUserProfileRepository>().UpsertAsync(new UserProfileEntity
            {
                Username = user,
                DisplayName = "Alex Beispiel",
                Formality = Formality.Formal,
                FreeText = ProfileMarker,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            return true;
        });

    private Task<AtelierEndToEndHost> StartAsync(IntakeMode mode, bool withWorker = true) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<OrderOptions>(o => o.IntakeMode = mode);
            services.PostConfigure<IntakeOptions>(o => o.PollInterval = TimeSpan.FromMilliseconds(200));
            if (!withWorker)
            {
                foreach (var worker in services.Where(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                                                           && d.ImplementationType?.Name == "OrderIntakeWorker").ToList())
                    services.Remove(worker);
            }
        });
}
