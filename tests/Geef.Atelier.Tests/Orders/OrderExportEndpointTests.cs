using System.Net;
using System.Text;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary><c>GET /orders/{id}/export/{format}</c> over the real wiring.</summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class OrderExportEndpointTests(PostgresFixture postgres)
{
    private const string Text = "# Thank you\n\nDear team, thank you for a great year.";

    [Fact]
    public async Task ExportsTheThreeFormatsToTheOwnerAndAdminsOnly()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var username = "owner-" + Guid.NewGuid().ToString("N")[..6];
        var owner = await host.CreateSignedInClientAsync(username);
        var stranger = await host.CreateSignedInClientAsync("stranger-" + Guid.NewGuid().ToString("N")[..6]);
        var admin = await host.CreateSignedInClientAsync("admin-" + Guid.NewGuid().ToString("N")[..6], isAdmin: true);

        var order = Order(username, "Write a thank-you note to the team");
        var empty = Order(username, "No text yet");
        await host.QueryAsync(async db =>
        {
            var repo = new OrderRepository(db);
            await repo.CreateWithFirstRunAsync(order, [], Run(username, order.Id, 1, status: RunStatus.Completed, finalText: "Old version"));
            await repo.AddRunAsync(order.Id, Run(username, status: RunStatus.Completed, finalText: Text));
            await repo.CreateWithFirstRunAsync(empty, [], Run(username, empty.Id, 1, status: RunStatus.Failed));
            return 0;
        });

        using (var md = await owner.GetAsync($"/orders/{order.Id}/export/md"))
        {
            Assert.Equal(HttpStatusCode.OK, md.StatusCode);
            Assert.Equal("text/markdown; charset=utf-8", md.Content.Headers.ContentType!.ToString());
            Assert.Equal("write-a-thank-you-note-to-the-team-v2.md", md.Content.Headers.ContentDisposition!.FileNameStar ?? md.Content.Headers.ContentDisposition.FileName);
            Assert.Equal(Text, Encoding.UTF8.GetString(await md.Content.ReadAsByteArrayAsync()));
        }

        using (var oldVersion = await owner.GetAsync($"/orders/{order.Id}/export/md?version=1"))
            Assert.Equal("Old version", await oldVersion.Content.ReadAsStringAsync());

        using (var docx = await owner.GetAsync($"/orders/{order.Id}/export/docx"))
        {
            Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", docx.Content.Headers.ContentType!.MediaType);
            var bytes = await docx.Content.ReadAsByteArrayAsync();
            Assert.Equal((byte)'P', bytes[0]);
            Assert.Equal((byte)'K', bytes[1]);
        }

        using (var pdf = await admin.GetAsync($"/orders/{order.Id}/export/pdf"))
        {
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
            Assert.StartsWith("%PDF", Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]), StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/orders/{order.Id}/export/md")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/orders/{order.Id}/export/exe")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/orders/{empty.Id}/export/md")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/orders/{order.Id}/export/md?version=7")).StatusCode);

        using var anonymous = host.CreateAnonymousClient();
        Assert.Equal(HttpStatusCode.Redirect, (await anonymous.GetAsync($"/orders/{order.Id}/export/md")).StatusCode);
    }
}
