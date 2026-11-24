using System.Net;
using System.Text;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Sources;

/// <summary><c>/api/sources</c> over the real wiring: cookie sign-in, antiforgery, limits, ownership and deletion.</summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class SourceEndpointsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task UploadAnswersWithAReceiptAndRefusesAnonymousForeignAndForgedRequests()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var owner = await host.CreateSignedInClientAsync("owner-" + Guid.NewGuid().ToString("N")[..6]);
        var stranger = await host.CreateSignedInClientAsync("stranger-" + Guid.NewGuid().ToString("N")[..6]);
        var admin = await host.CreateSignedInClientAsync("admin-" + Guid.NewGuid().ToString("N")[..6], isAdmin: true);
        var pdf = SyntheticDocuments.Pdf(2, p => string.Concat(Enumerable.Repeat($"Page {p} of the tender explains the conditions. ", 10)));

        using (var anonymous = host.CreateAnonymousClient())
        using (var response = await anonymous.PostAsync("/api/sources", new MultipartFormDataContent { { new ByteArrayContent(pdf), "file", "a.pdf" } }))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }

        using (var forged = await host.PostSourceAsync(owner, "a.pdf", pdf, withAntiforgery: false))
            Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

        string location;
        using (var created = await host.PostSourceAsync(owner, "Tender.pdf", pdf))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            location = created.Headers.Location!.ToString();
            var body = await created.Content.ReadAsStringAsync();
            Assert.Contains("\"kind\":\"pdf\"", body, StringComparison.Ordinal);
            Assert.Contains("\"budgetClass\":\"fullText\"", body, StringComparison.Ordinal);
            Assert.Contains("\"usable\":true", body, StringComparison.Ordinal);
        }

        var duplicate = await host.UploadSourceAsync(owner, "Tender copy.pdf", pdf);
        Assert.True(duplicate.GetProperty("duplicate").GetBoolean());
        Assert.Equal(location, $"/api/sources/{duplicate.GetProperty("sourceId").GetGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(location)).StatusCode);

        var exe = await host.UploadSourceAsync(owner, "invoice.pdf", [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00]);
        Assert.False(exe.GetProperty("usable").GetBoolean());
        Assert.Equal(JsonValueKindNull, exe.GetProperty("sourceId").ValueKind);
        Assert.False(string.IsNullOrEmpty(exe.GetProperty("reason").GetString()));

        using var crossSite = new HttpRequestMessage(HttpMethod.Post, "/api/sources")
        {
            Content = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("text")), "file", "a.txt" } },
        };
        crossSite.Headers.Add(AtelierEndToEndHost.AntiforgeryHeader, await host.GetAntiforgeryTokenAsync(owner));
        crossSite.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.SendAsync(crossSite)).StatusCode);
    }

    [Fact]
    public async Task TooLargeUploadsAre413AndAttachedSourcesCannotBeDeleted()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var username = "owner-" + Guid.NewGuid().ToString("N")[..6];
        var owner = await host.CreateSignedInClientAsync(username);

        using (var huge = await host.PostSourceAsync(owner, "huge.txt", new byte[26 * 1_048_576 + 1]))
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
            Assert.Contains("\"reason\"", await huge.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var justOver = await host.PostSourceAsync(owner, "over.txt", new byte[25 * 1_048_576 + 10]))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, justOver.StatusCode);

        var attached = (await host.UploadSourceAsync(owner, "a.md", Encoding.UTF8.GetBytes("Attached notes " + Guid.NewGuid()))).GetProperty("sourceId").GetGuid();
        var loose = (await host.UploadSourceAsync(owner, "b.md", Encoding.UTF8.GetBytes("Loose notes " + Guid.NewGuid()))).GetProperty("sourceId").GetGuid();
        var order = Order(username);
        await host.QueryAsync(async db =>
        {
            db.Orders.Add(order);
            db.OrderSources.Add(Link(order.Id, attached));
            return await db.SaveChangesAsync();
        });

        var token = await host.GetAntiforgeryTokenAsync(owner);
        Assert.Equal(HttpStatusCode.Conflict, (await SendDeleteAsync(owner, attached, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendDeleteAsync(owner, loose, null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendDeleteAsync(owner, loose, token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendDeleteAsync(owner, loose, token)).StatusCode);
        Assert.False(await host.QueryAsync(db => db.Sources.AnyAsync(s => s.Id == loose)));
        Assert.Equal(0, await host.QueryAsync(db => db.Sources.CountAsync(s => s.OwnerUsername == username && s.FileName == "huge.txt")));
    }

    [Fact]
    public async Task AnotherPersonCannotDeleteASourceButAnAdministratorCan()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var username = "owner-" + Guid.NewGuid().ToString("N")[..6];
        var owner = await host.CreateSignedInClientAsync(username);
        var stranger = await host.CreateSignedInClientAsync("stranger-" + Guid.NewGuid().ToString("N")[..6]);
        var admin = await host.CreateSignedInClientAsync("admin-" + Guid.NewGuid().ToString("N")[..6], isAdmin: true);
        var loose = (await host.UploadSourceAsync(owner, "loose.md", Encoding.UTF8.GetBytes("Loose notes " + Guid.NewGuid()))).GetProperty("sourceId").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await SendDeleteAsync(stranger, loose, await host.GetAntiforgeryTokenAsync(stranger))).StatusCode);

        Assert.True(await host.QueryAsync(db => db.Sources.AnyAsync(s => s.Id == loose && s.StoragePath != null)));
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/sources/{loose}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await SendDeleteAsync(admin, loose, await host.GetAntiforgeryTokenAsync(admin))).StatusCode);
        Assert.False(await host.QueryAsync(db => db.Sources.AnyAsync(s => s.Id == loose)));
    }

    [Fact]
    public async Task ASignInWithoutUserNameIsRefusedAndNeverSeesOrChangesOtherFiles()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var owner = await host.CreateSignedInClientAsync("owner-" + Guid.NewGuid().ToString("N")[..6]);
        var nameless = await host.CreateSignedInClientAsync("nameless-" + Guid.NewGuid().ToString("N")[..6], withName: false);
        var loose = (await host.UploadSourceAsync(owner, "loose.md", Encoding.UTF8.GetBytes("Loose notes " + Guid.NewGuid()))).GetProperty("sourceId").GetGuid();
        var sourcesBefore = await host.QueryAsync(db => db.Sources.CountAsync());

        using (var read = await nameless.GetAsync($"/api/sources/{loose}"))
        {
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
            Assert.Contains(OwnerScope.UnknownUserMessage, await read.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await SendDeleteAsync(nameless, loose, await host.GetAntiforgeryTokenAsync(nameless))).StatusCode);
        Assert.True(await host.QueryAsync(db => db.Sources.AnyAsync(s => s.Id == loose)));

        using (var upload = await host.PostSourceAsync(nameless, "mine.md", Encoding.UTF8.GetBytes("Nameless notes " + Guid.NewGuid())))
            Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);
        Assert.Equal(sourcesBefore, await host.QueryAsync(db => db.Sources.CountAsync()));
    }

    private const System.Text.Json.JsonValueKind JsonValueKindNull = System.Text.Json.JsonValueKind.Null;

    private static async Task<HttpResponseMessage> SendDeleteAsync(HttpClient client, Guid sourceId, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/sources/{sourceId}");
        if (token is not null)
            request.Headers.Add(AtelierEndToEndHost.AntiforgeryHeader, token);
        return await client.SendAsync(request);
    }
}
