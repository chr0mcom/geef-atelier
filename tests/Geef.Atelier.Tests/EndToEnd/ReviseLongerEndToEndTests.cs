using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Tests.Mcp.Orders;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Changing a text without the button “Longer” over the real wiring: <c>revise_order</c> with the quick change <c>longer</c> answers with the
/// sentence that a longer text needs new details and starts no run; asking for a longer text in one's own words is made, and when the new
/// version is clearly longer the result says so on the service and in <c>get_order_result</c>, while the next version of the same length
/// does not. The writer gets the measured revision sentence unchanged.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ReviseLongerEndToEndTests(PostgresFixture postgres)
{
    private const string User = "e2e-revise-longer";

    private const string Request = "Schreibe eine kurze Einladung zum Elternabend der Klasse 3b am 12. Mai um 19 Uhr in der Aula, max. 80 Wörter.";

    private const string FirstText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 12. Mai um 19 Uhr in der Aula.\n\nWir freuen uns auf Sie.";

    private const string LongerText =
        "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 12. Mai um 19 Uhr in der Aula. Wir sprechen über das kommende Halbjahr.\n\n" +
        "Wir freuen uns auf Sie und auf ein gutes Gespräch.";

    private const string LongerDateText =
        "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 5. Mai um 19 Uhr in der Aula. Wir sprechen über das kommende Halbjahr.\n\n" +
        "Wir freuen uns auf Sie und auf ein gutes Gespräch.";

    private const string AskedLonger = "Bitte mach den Text länger.";

    [Fact]
    public async Task Longer_needs_details_and_a_clearly_longer_version_says_so_on_every_surface()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
            services.PostConfigure<OrderOptions>(o => o.Revise = ReviseMode.On));
        host.Script.EnqueueExecutorText(FirstText);
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, User, OrderEntry.Web, null));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        var orderId = submission.OrderId;
        var owner = new FakeUser(User);

        var refused = await host.WithScopeAsync(async sp =>
        {
            var orders = sp.GetRequiredService<IOrderService>();
            return await Assert.ThrowsAsync<McpException>(() => ReviseOrderTool.ReviseOrder(orders, owner, orderId.ToString(), quick: ["longer"]));
        });
        Assert.Equal(ReviseOrderTool.LongerMessage, refused.Message);
        Assert.Contains(RevisionLines.LongerNeedsDetails, refused.Message, StringComparison.Ordinal);
        Assert.Equal(1, await host.QueryAsync(db => db.Runs.CountAsync(r => r.OrderId == orderId)));

        var firstResult = await ResultDtoAsync(host, owner, orderId);
        Assert.Null(firstResult.LengthNotice);

        var callsBefore = host.Llm.Calls.Count;
        host.Script.EnqueueExecutorText(LongerText);
        await ReviseAsync(host, orderId, AskedLonger);

        var writer = Assert.Single(host.Llm.Calls.Skip(callsBefore), c => c.Role == LlmRole.Executor);
        Assert.Contains(DirectWriterPrompts.RevisionInstruction, writer.Messages.Last(m => m.Role == "user").Content, StringComparison.Ordinal);
        Assert.Contains(AskedLonger, writer.Messages.Last(m => m.Role == "user").Content, StringComparison.Ordinal);

        var longer = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal((2, LongerText), (longer!.Version, longer.Text));
        Assert.Equal(RevisionLines.LongerThanBefore, longer.LengthNotice);
        var longerDto = await ResultDtoAsync(host, owner, orderId);
        Assert.Equal("This version is longer than the one before. Anything new in it did not come from you – please check it.", longerDto.LengthNotice);

        host.Script.EnqueueExecutorText(LongerDateText);
        await ReviseAsync(host, orderId, "The meeting is on 5 May, not 12 May.");

        var third = await ResultDtoAsync(host, owner, orderId);
        Assert.Equal(3, third.Version);
        Assert.Null(third.LengthNotice);
        var runs = await host.QueryAsync(db => db.Runs.AsNoTracking().Where(r => r.OrderId == orderId).OrderBy(r => r.OrderVersion).ToListAsync());
        Assert.Equal([false, true, false], runs.Select(r => r.GrewBeyondItsBase()));
        Assert.All(runs, r => Assert.Equal(RunStatus.Completed, r.Status));
    }

    private static async Task ReviseAsync(AtelierEndToEndHost host, Guid orderId, string change)
    {
        await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().ReviseAsync(new ReviseOrderRequest(orderId, User, change, [], OrderEntry.Web)));
        host.ReleaseOrchestrator();
        await host.WaitForOrderFinishedAsync(orderId);
    }

    private static Task<Geef.Atelier.Mcp.Models.OrderResultDto> ResultDtoAsync(AtelierEndToEndHost host, FakeUser owner, Guid orderId) =>
        host.WithScopeAsync(sp => GetOrderResultTool.GetOrderResult(sp.GetRequiredService<IOrderService>(), owner, orderId.ToString()));
}
