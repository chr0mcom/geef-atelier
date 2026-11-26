using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The upload ticket end to end over the real <c>Program</c> wiring with the real ticket service, intake and database:
/// a token upload stores exactly the sent bytes, a ticket is used once, hostile and scanned files are refused and counted,
/// the page upload belongs only to its owner.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class UploadTicketEndToEndTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ATokenUploadStoresExactlyTheSentBytesAndTheTicketIsUsedOnce()
    {
        await using var host = await StartAsync();
        var owner = "token-" + Guid.NewGuid().ToString("N")[..6];
        var pdf = SyntheticDocuments.Pdf(40, p => string.Concat(Enumerable.Repeat($"Page {p} of the tender explains the conditions in detail. ", 60)));
        var local = Convert.ToHexStringLower(SHA256.HashData(pdf));
        var prepared = await PrepareAsync(host, owner, "Tender.pdf", pdf.Length);
        using var client = host.CreateAnonymousClient();

        using (var created = await PutAsync(client, prepared.UploadId, pdf, prepared.Token))
        {
            var raw = await created.Content.ReadAsStringAsync();
            Assert.True(created.StatusCode == HttpStatusCode.Created, raw);
            using var body = JsonDocument.Parse(raw);
            Assert.Equal(local, body.RootElement.GetProperty("sha256").GetString());
            Assert.Equal(prepared.UploadId, body.RootElement.GetProperty("uploadId").GetGuid());
            Assert.True(body.RootElement.GetProperty("usable").GetBoolean());
            Assert.Contains(prepared.UploadId.ToString(), body.RootElement.GetProperty("next").GetString(), StringComparison.Ordinal);
            var sourceId = body.RootElement.GetProperty("sourceId").GetGuid();
            Assert.Equal(local, await host.QueryAsync(db => db.Sources.Where(s => s.Id == sourceId).Select(s => s.Sha256).SingleAsync()));
            Assert.Equal(owner, await host.QueryAsync(db => db.Sources.Where(s => s.Id == sourceId).Select(s => s.OwnerUsername).SingleAsync()));
        }

        Assert.Equal("Used", await TicketStateAsync(host, prepared.UploadId));
        using (var again = await PutAsync(client, prepared.UploadId, pdf, prepared.Token))
            await AssertReasonAsync(again, HttpStatusCode.NotFound, UploadTicketLines.LinkNotValid);

        var receipts = await host.WithScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().ResolveAsync([prepared.UploadId], owner, id => $"/u/{id}"));
        Assert.Equal("Tender.pdf", Assert.Single(receipts).FileName);
    }

    [Fact]
    public async Task OfTenParallelUploadsWithTheSameTokenExactlyOneIsStored()
    {
        await using var host = await StartAsync();
        var owner = "race-" + Guid.NewGuid().ToString("N")[..6];
        var bytes = Encoding.UTF8.GetBytes("# Notes\n" + string.Concat(Enumerable.Repeat("The delivery moves by two weeks. ", 2_000)));
        var prepared = await PrepareAsync(host, owner, "notes.md", null);
        using var client = host.CreateAnonymousClient();

        var answers = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            using var response = await PutAsync(client, prepared.UploadId, bytes, prepared.Token);
            return response.StatusCode;
        }));

        Assert.Equal(1, answers.Count(s => s == HttpStatusCode.Created));
        Assert.All(answers.Where(s => s != HttpStatusCode.Created), s => Assert.Contains(s, new[] { HttpStatusCode.NotFound, HttpStatusCode.Conflict }));
        Assert.Equal(1, await host.QueryAsync(db => db.Sources.CountAsync(s => s.OwnerUsername == owner)));
        Assert.Equal("Used", await TicketStateAsync(host, prepared.UploadId));
    }

    [Fact]
    public async Task AHostileWordFileOverTheTokenIsRefusedAndTheHostKeepsAnswering()
    {
        await using var host = await StartAsync();
        var owner = "bomb-" + Guid.NewGuid().ToString("N")[..6];
        var prepared = await PrepareAsync(host, owner, "deep.docx", null);
        using var client = host.CreateAnonymousClient();

        using (var bomb = await PutAsync(client, prepared.UploadId, HostileDocuments.DeeplyNestedDocx(20_000), prepared.Token))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, bomb.StatusCode);
            Assert.Contains("\"usable\":false", await bomb.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var health = await client.GetAsync("/health"))
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(0, await host.QueryAsync(db => db.Sources.CountAsync(s => s.OwnerUsername == owner)));
        Assert.Equal("Open", await TicketStateAsync(host, prepared.UploadId));
    }

    [Fact]
    public async Task AScannedPdfIsRefusedWithTheKnownSentenceAndCountedOncePerWay()
    {
        await using var host = await StartAsync();
        var owner = "scan-" + Guid.NewGuid().ToString("N")[..6];
        var scan = SyntheticDocuments.Pdf(2, _ => string.Empty);
        var byToken = await PrepareAsync(host, owner, "scan.pdf", null);
        var byPage = await PrepareAsync(host, owner, "scan.pdf", null);
        var signedIn = await host.CreateSignedInClientAsync(owner);
        using var client = host.CreateAnonymousClient();

        using (var token = await PutAsync(client, byToken.UploadId, scan, byToken.Token))
        {
            var raw = await token.Content.ReadAsStringAsync();
            await AssertReasonAsync(token, HttpStatusCode.UnprocessableEntity, PdfSourceExtractor.NoTextReason);
            using var body = JsonDocument.Parse(raw);
            Assert.Equal(UploadTicketLines.AfterRefusal, body.RootElement.GetProperty("next").GetString());
            Assert.False(body.RootElement.TryGetProperty("fileName", out _));
            Assert.False(body.RootElement.TryGetProperty("preview", out _));
        }
        using (var page = await PostPageAsync(host, signedIn, byPage.UploadId, "scan.pdf", scan))
            await AssertReasonAsync(page, HttpStatusCode.UnprocessableEntity, PdfSourceExtractor.NoTextReason);

        Assert.Equal(0, await host.QueryAsync(db => db.Sources.CountAsync(s => s.OwnerUsername == owner)));
        var rows = await RefusalsAsync(host, owner);
        Assert.Equal(["mcp_ticket:pdf_no_text", "upload_page:pdf_no_text"], rows.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task AnUnusableFileOnTheStartPageIsCountedOnTheWebWayAndNoOther()
    {
        await using var host = await StartAsync();
        var owner = "web-" + Guid.NewGuid().ToString("N")[..6];
        var signedIn = await host.CreateSignedInClientAsync(owner);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 };

        using (var refused = await host.PostSourceAsync(signedIn, "Holiday photo.png", png))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        using (var scan = await host.PostSourceAsync(signedIn, "Scan.pdf", SyntheticDocuments.Pdf(2, _ => string.Empty)))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, scan.StatusCode);

        Assert.Equal(["web:pdf_no_text", "web:unsupported_type"], (await RefusalsAsync(host, owner)).Order(StringComparer.Ordinal).ToArray());
        var extensions = await host.QueryAsync(db => db.SourceRefusals.Where(r => r.OwnerUsername == owner).Select(r => r.Extension).ToListAsync());
        Assert.Equal(new string?[] { "pdf", "png" }, extensions.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task OverloadOnTheTokenWayUsesNoAttemptOverTheRealWiring()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.Configure<UploadOptions>(o => { o.Tickets = UploadTicketsMode.On; o.MaxAttempts = 1; });
            services.Configure<SourceOptions>(o => o.MaxConcurrentUploadsPerUser = 1);
        });
        var owner = "busy-" + Guid.NewGuid().ToString("N")[..6];
        var holder = await PrepareAsync(host, owner, "holder.md", null);
        var waiting = await PrepareAsync(host, owner, "waiting.md", null);
        using var client = host.CreateAnonymousClient();
        var stall = new StalledContent(Encoding.UTF8.GetBytes("# Holder\nThe first bytes arrive, then the rest. "));
        using var holding = new HttpRequestMessage(HttpMethod.Put, $"/api/uploads/{holder.UploadId}") { Content = stall };
        holding.Headers.Add(UploadTicketRoutes.TokenHeader, holder.Token);
        var held = client.SendAsync(holding);
        await stall.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (await TicketStateAsync(host, holder.UploadId) != "Receiving" && watch.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(20);

        for (var i = 0; i < 3; i++)
        {
            using var busy = await PutAsync(client, waiting.UploadId, Encoding.UTF8.GetBytes("# Waiting\n" + waiting.UploadId), waiting.Token);
            var raw = await busy.Content.ReadAsStringAsync();
            Assert.True(busy.StatusCode == HttpStatusCode.TooManyRequests, raw);
            using var body = JsonDocument.Parse(raw);
            Assert.Equal(UploadTicketLines.AfterNotChecked, body.RootElement.GetProperty("next").GetString());
        }
        Assert.Equal(("Open", 0), (await TicketStateAsync(host, waiting.UploadId),
            await host.QueryAsync(db => db.Set<UploadTicketEntity>().Where(t => t.Id == waiting.UploadId).Select(t => t.Attempts).SingleAsync())));

        stall.Stop.TrySetResult();
        using (var done = await held)
            Assert.Equal(HttpStatusCode.Created, done.StatusCode);
        using (var arrived = await PutAsync(client, waiting.UploadId, Encoding.UTF8.GetBytes("# Waiting\n" + waiting.UploadId), waiting.Token))
            Assert.Equal(HttpStatusCode.Created, arrived.StatusCode);
    }

    [Fact]
    public async Task ThePageUploadBelongsOnlyToItsOwnerAndTheTypeComesFromTheContent()
    {
        await using var host = await StartAsync();
        var owner = "page-" + Guid.NewGuid().ToString("N")[..6];
        var signedIn = await host.CreateSignedInClientAsync(owner);
        var stranger = await host.CreateSignedInClientAsync("stranger-" + Guid.NewGuid().ToString("N")[..6]);
        var prepared = await PrepareAsync(host, owner, "letter.md", null);
        var bytes = Encoding.UTF8.GetBytes("# Letter\nThe rent goes up in March. " + Guid.NewGuid());
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 };

        using (var foreign = await PostPageAsync(host, stranger, prepared.UploadId, "letter.md", bytes))
            await AssertReasonAsync(foreign, HttpStatusCode.NotFound, "Not found.");
        Assert.Contains("Not Found", await stranger.GetStringAsync($"/u/{prepared.UploadId}"), StringComparison.Ordinal);
        Assert.DoesNotContain("letter.md", await stranger.GetStringAsync($"/u/{prepared.UploadId}"), StringComparison.Ordinal);

        using (var image = await PostPageAsync(host, signedIn, prepared.UploadId, "picture.pdf", png))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, image.StatusCode);
        Assert.Equal("Open", await TicketStateAsync(host, prepared.UploadId));

        using (var created = await PostPageAsync(host, signedIn, prepared.UploadId, "../../etc/letter.md", bytes))
        {
            var raw = await created.Content.ReadAsStringAsync();
            Assert.True(created.StatusCode == HttpStatusCode.Created, raw);
            using var body = JsonDocument.Parse(raw);
            Assert.DoesNotContain("/", body.RootElement.GetProperty("fileName").GetString(), StringComparison.Ordinal);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), body.RootElement.GetProperty("sha256").GetString());
        }
        Assert.Equal("Used", await TicketStateAsync(host, prepared.UploadId));
        Assert.Equal(["upload_page:unsupported_type"], await RefusalsAsync(host, owner));
    }

    [Fact]
    public async Task AFileAboveTheLimitOverTheTokenIs413AndNothingIsStored()
    {
        await using var host = await StartAsync();
        var owner = "large-" + Guid.NewGuid().ToString("N")[..6];
        var prepared = await PrepareAsync(host, owner, "large.txt", null);
        using var client = host.CreateAnonymousClient();

        using (var tooLarge = await PutAsync(client, prepared.UploadId, new byte[25 * 1_048_576 + 1], prepared.Token))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);

        Assert.Equal(0, await host.QueryAsync(db => db.Sources.CountAsync(s => s.OwnerUsername == owner)));
        Assert.Equal(["mcp_ticket:too_large"], await RefusalsAsync(host, owner));
    }

    [Fact]
    public async Task WithTheShippedSwitchOffTheRoutesAnswerLikeUnknownAddresses()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var owner = await host.CreateSignedInClientAsync("off-" + Guid.NewGuid().ToString("N")[..6]);
        var id = Guid.NewGuid();
        using var client = host.CreateAnonymousClient();

        Assert.False(await host.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<IUploadTicketService>().Enabled)));
        using (var put = await PutAsync(client, id, [1, 2, 3], "any-token"))
        using (var unknown = await client.PutAsync($"/api/uploads-unknown/{id}", new ByteArrayContent([1, 2, 3])))
            Assert.Equal(unknown.StatusCode, put.StatusCode);
        using (var page = await PostPageAsync(host, owner, id, "a.md", [1, 2, 3]))
        using (var unknownPage = await owner.PostAsync($"/api/uploads-unknown/{id}/page", new ByteArrayContent([1])))
            Assert.Equal(unknownPage.StatusCode, page.StatusCode);
        using (var view = await owner.GetAsync($"/u/{id}"))
            Assert.Equal(HttpStatusCode.NotFound, view.StatusCode);
    }

    private Task<AtelierEndToEndHost> StartAsync() =>
        AtelierEndToEndHost.StartAsync(postgres, services => services.Configure<UploadOptions>(o => o.Tickets = UploadTicketsMode.On));

    private static Task<PreparedUpload> PrepareAsync(AtelierEndToEndHost host, string owner, string fileName, long? sizeBytes) =>
        host.WithScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().PrepareAsync(owner, fileName, sizeBytes));

    private static Task<string> TicketStateAsync(AtelierEndToEndHost host, Guid id) =>
        host.QueryAsync(db => db.Database.SqlQuery<string>($"SELECT \"State\" AS \"Value\" FROM \"UploadTickets\" WHERE \"Id\" = {id}").SingleAsync());

    private static async Task<string[]> RefusalsAsync(AtelierEndToEndHost host, string owner) =>
        (await host.QueryAsync(db => db.Database
            .SqlQuery<string>($"SELECT \"Path\" || ':' || \"Code\" AS \"Value\" FROM \"SourceRefusals\" WHERE \"OwnerUsername\" = {owner}")
            .ToListAsync())).ToArray();

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, byte[] bytes, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/uploads/{id}") { Content = new ByteArrayContent(bytes) };
        request.Headers.Add(UploadTicketRoutes.TokenHeader, token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostPageAsync(AtelierEndToEndHost host, HttpClient client, Guid id, string fileName, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/uploads/{id}/page") { Content = form };
        request.Headers.Add(AtelierEndToEndHost.AntiforgeryHeader, await host.GetAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    private sealed class StalledContent(byte[] head) : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Stop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(head);
            await stream.FlushAsync();
            Started.TrySetResult();
            await Stop.Task;
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes("Done."));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private static async Task AssertReasonAsync(HttpResponseMessage response, HttpStatusCode status, string reason)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, $"Expected {(int)status}, got {(int)response.StatusCode}: {raw}");
        using var body = JsonDocument.Parse(raw);
        Assert.Equal(reason, body.RootElement.GetProperty("reason").GetString());
    }
}
