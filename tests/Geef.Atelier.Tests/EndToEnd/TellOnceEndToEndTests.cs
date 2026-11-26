using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Models;
using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// "Tell Atelier once" over the real wiring (intake worker, clarifier and writer scripted, database, MCP endpoint real), with two
/// accounts: account A gets a letter whose sender and signature were assumed, is offered to save them (MCP <c>aboutMeMissing</c>),
/// saves them with <c>profile</c>, and its next letter of the same kind has no assumption about them, the writer gets the signature word
/// for word and the result says what came from About me. Account B and an administrator looking at A's order see none of it, and B's
/// writer never gets A's details. With <c>Orders:TellOnce = Off</c> nothing of it shows and the assistant cannot save.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class TellOnceEndToEndTests(PostgresFixture postgres)
{
    private const string FirstRequest = "Schreib eine kurze E-Mail an die Hausverwaltung: Die Heizung in meiner Wohnung ist seit gestern kalt, bitte um einen Termin.";
    private const string SecondRequest = "Schreib eine kurze E-Mail an die Hausverwaltung: Im Treppenhaus ist seit Montag das Licht kaputt, bitte um Reparatur.";
    private const string Name = "Erika Beispiel";
    private const string Signature = "Viele Grüße\nErika Beispiel\nWohnung 2b";
    private const string AccountB = "tell-once-account-b";

    private const string LetterClarification =
        """{"text_type":"E-Mail","language":"de","understanding":"Eine kurze E-Mail an die Hausverwaltung.","is_revision":false,"revision_file":null,"length_scale":"standard","gaps":[""" +
        """{"topic":"sender","resolution":"memory","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Absender: Mieterin oder Mieter, Name als Platzhalter","confidence":"unsure","question":null},""" +
        """{"topic":"signature","resolution":"assume","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Unterschrift: [Ihr Name]","confidence":"unsure","question":null},""" +
        """{"topic":"tone","resolution":"assume","user_can_know":true,"web_researchable":false,"impact":"low","assumption":"Höflich und sachlich","confidence":"sure","question":null}""" +
        """],"research_topics":[]}""";

    [Fact]
    public async Task Saved_once_the_next_letter_assumes_nothing_about_it_and_no_other_account_sees_anything()
    {
        await using var host = await StartAsync(TellOnceMode.On);
        var accountA = AtelierEndToEndHost.McpTokenUser;

        host.Script.EnqueueClarifierJson(LetterClarification);
        host.Script.EnqueueExecutorText("Sehr geehrte Damen und Herren,\n\ndie Heizung ist kalt. Bitte nennen Sie mir einen Termin.\n\nViele Grüße\n[Ihr Name]");
        var first = await SubmitOverMcpAsync(host, FirstRequest);

        var firstResult = await host.CallMcpToolAsync("get_order_result", new { orderId = first.ToString() });
        Assert.Equal(["sender", "signature"], Strings(firstResult, "aboutMeMissing"));
        Assert.True(Missing(firstResult, "fromAboutMe"), firstResult.ToString());
        Assert.Contains(OrderDtoMappings.AboutMeMissingNote, firstResult.GetProperty("note").GetString(), StringComparison.Ordinal);
        var asAdmin = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(first, null, "e2e-admin"));
        Assert.NotNull(asAdmin);
        Assert.Null(asAdmin.TellOnce);
        Assert.Null(asAdmin.FromAboutMe);
        Assert.Null(await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(first, AccountB)));

        var saved = await host.CallMcpToolAsync("profile", new { name = Name, signature = Signature });
        Assert.Equal((Name, Signature, ProfileTool.SavedNote), (saved.GetProperty("name").GetString(), saved.GetProperty("signature").GetString(), saved.GetProperty("note").GetString()));

        host.Script.EnqueueClarifierJson(LetterClarification);
        host.Script.EnqueueExecutorText("Sehr geehrte Damen und Herren,\n\nim Treppenhaus ist das Licht kaputt. Bitte reparieren Sie es.\n\n" + Signature);
        var writerCallsBefore = host.Llm.CallsFor(LlmRole.Executor).Count;
        var second = await SubmitOverMcpAsync(host, SecondRequest);

        var record = IntakeRecordJson.TryParse(await host.QueryAsync(db => db.Orders.AsNoTracking().Where(o => o.Id == second).Select(o => o.ClarifyJson).SingleAsync()));
        Assert.NotNull(record);
        Assert.DoesNotContain(record.Assumptions, a => a.Topic is GapTopic.Sender or GapTopic.Signature);
        Assert.Equal([GapTopic.Sender, GapTopic.Signature], record.FromMemory.Select(m => m.Topic).Order());
        Assert.Empty(record.Questions);
        var secondWriter = host.Llm.CallsFor(LlmRole.Executor).Skip(writerCallsBefore).Select(c => c.RawBody).ToList();
        Assert.Contains(secondWriter, body => JsonText(body).Contains(Signature, StringComparison.Ordinal));
        var secondResult = await host.CallMcpToolAsync("get_order_result", new { orderId = second.ToString() });
        Assert.True(Missing(secondResult, "aboutMeMissing"), secondResult.ToString());
        Assert.Equal(["sender", "signature"], Strings(secondResult, "fromAboutMe"));
        Assert.Contains(Signature, secondResult.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Contains(OrderDtoMappings.FromAboutMeNote, secondResult.GetProperty("note").GetString(), StringComparison.Ordinal);

        host.Script.EnqueueClarifierJson(LetterClarification);
        var writerCallsBeforeB = host.Llm.CallsFor(LlmRole.Executor).Count;
        var submissionB = await host.SubmitOrderAsync(new SubmitOrderRequest(FirstRequest, AccountB, OrderEntry.Web));
        await host.WaitForOrderFinishedAsync(submissionB.OrderId);

        var resultB = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(submissionB.OrderId, AccountB));
        Assert.Equal([GapTopic.Sender, GapTopic.Signature], resultB!.TellOnce);
        Assert.Null(resultB.FromAboutMe);
        Assert.True((await host.WithScopeAsync(sp => sp.GetRequiredService<IProfileService>().GetAsync(AccountB))).IsEmpty);
        Assert.Null(await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(second, AccountB)));
        var writerB = host.Llm.CallsFor(LlmRole.Executor).Skip(writerCallsBeforeB).Select(c => JsonText(c.RawBody)).ToList();
        Assert.NotEmpty(writerB);
        Assert.DoesNotContain(writerB, body => body.Contains(Name, StringComparison.Ordinal) || body.Contains("Wohnung 2b", StringComparison.Ordinal));
        Assert.Equal(Name, (await host.WithScopeAsync(sp => sp.GetRequiredService<IProfileService>().GetAsync(accountA))).Values.DisplayName);
    }

    [Fact]
    public async Task Switched_off_the_result_shows_nothing_of_it_and_the_assistant_cannot_save()
    {
        await using var host = await StartAsync(TellOnceMode.Off);

        host.Script.EnqueueClarifierJson(LetterClarification);
        var orderId = await SubmitOverMcpAsync(host, FirstRequest);

        var result = await host.CallMcpToolAsync("get_order_result", new { orderId = orderId.ToString() });
        Assert.False(result.TryGetProperty("aboutMeMissing", out _), result.ToString());
        Assert.False(result.TryGetProperty("fromAboutMe", out _), result.ToString());
        var owner = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, AtelierEndToEndHost.McpTokenUser));
        Assert.Null(owner!.TellOnce);
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => host.CallMcpToolAsync("profile", new { signature = Signature }));
        Assert.Contains(ProfileTool.SavingOffMessage, refused.Message, StringComparison.Ordinal);
        Assert.True((await host.CallMcpToolAsync("profile", new { })).GetProperty("isEmpty").GetBoolean());
    }

    private static async Task<Guid> SubmitOverMcpAsync(AtelierEndToEndHost host, string request)
    {
        var submitted = await host.CallMcpToolAsync("submit_order", new { request });
        host.ReleaseOrchestrator();
        var orderId = Guid.Parse(submitted.GetProperty("orderId").GetString()!);
        Assert.NotEqual("needs_input", submitted.GetProperty("state").GetString());
        await host.WaitForOrderFinishedAsync(orderId);
        return orderId;
    }

    private static bool Missing(JsonElement element, string name) =>
        !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static IReadOnlyList<string?> Strings(JsonElement element, string name) =>
        element.GetProperty(name).EnumerateArray().Select(e => e.GetString()).ToList();

    private static string JsonText(string body)
    {
        try
        {
            return string.Join('\n', Texts(JsonDocument.Parse(body).RootElement));
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static IEnumerable<string> Texts(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => Texts(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Texts),
        JsonValueKind.String => [element.GetString() ?? string.Empty],
        _ => [],
    };

    private Task<AtelierEndToEndHost> StartAsync(TellOnceMode mode) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<OrderOptions>(o =>
            {
                o.IntakeMode = IntakeMode.On;
                o.TellOnce = mode;
            });
            services.PostConfigure<IntakeOptions>(o => o.PollInterval = TimeSpan.FromMilliseconds(200));
        });
}
