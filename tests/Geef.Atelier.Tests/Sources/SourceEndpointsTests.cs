using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

    [Fact]
    public async Task APersonRunsAtMostTwoUploadsAtOnceWhileOthersStillGetThrough()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services => services.Configure<SourceOptions>(o => o.MaxConcurrentIntakes = 3));
        var busy = await host.CreateSignedInClientAsync("busy-" + Guid.NewGuid().ToString("N")[..6]);
        var other = await host.CreateSignedInClientAsync("other-" + Guid.NewGuid().ToString("N")[..6]);
        var release = new TaskCompletionSource();
        var first = SendStalledAsync(host, busy, "first.txt", release.Task);
        var second = SendStalledAsync(host, busy, "second.txt", release.Task);
        await WaitForIncomingAsync(host, 2);

        using (var third = await host.PostSourceAsync(busy, "third.md", Encoding.UTF8.GetBytes("Third " + Guid.NewGuid())))
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
            Assert.Equal("5", third.Headers.GetValues("Retry-After").Single());
            Assert.Contains("You are already adding 2 files at the same time. Please wait until one is done, then try again.", await third.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var meanwhile = await host.PostSourceAsync(other, "other.md", Encoding.UTF8.GetBytes("Other person " + Guid.NewGuid())))
            Assert.Equal(HttpStatusCode.Created, meanwhile.StatusCode);

        release.SetResult();
        using (var done = await first)
            Assert.Equal(HttpStatusCode.Created, done.StatusCode);
        using (var done = await second)
            Assert.Equal(HttpStatusCode.Created, done.StatusCode);
        using var later = await host.PostSourceAsync(busy, "third.md", Encoding.UTF8.GetBytes("Third " + Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, later.StatusCode);
    }

    [Fact]
    public async Task AnUploadThatStallsIsCutOffAfterTheTimeAndFreesItsSlot()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services => services.Configure<SourceOptions>(o =>
        {
            o.MaxConcurrentIntakes = 1;
            o.ExtractionQueueTimeout = TimeSpan.FromMilliseconds(500);
            o.UploadTimeout = TimeSpan.FromSeconds(2);
        }));
        var slow = await host.CreateSignedInClientAsync("slow-" + Guid.NewGuid().ToString("N")[..6]);
        var other = await host.CreateSignedInClientAsync("other-" + Guid.NewGuid().ToString("N")[..6]);
        var never = new TaskCompletionSource();
        var clock = Stopwatch.StartNew();

        var stalled = SendStalledAsync(host, slow, "stalled.txt", never.Task);
        await WaitForIncomingAsync(host, 1);
        using (var blocked = await host.PostSourceAsync(other, "blocked.md", Encoding.UTF8.GetBytes("Blocked " + Guid.NewGuid())))
            Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);

        using (var cut = await stalled.WaitAsync(TimeSpan.FromSeconds(60)))
        {
            Assert.Equal(HttpStatusCode.RequestTimeout, cut.StatusCode);
            Assert.Contains(Geef.Atelier.Web.Endpoints.SourceEndpoints.TimeoutReason, await cut.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), $"took {clock.Elapsed}");
        using var after = await host.PostSourceAsync(other, "after.md", Encoding.UTF8.GetBytes("After " + Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, after.StatusCode);
        Assert.Equal(0, await IncomingCountAsync(host));
        never.TrySetResult();
    }

    [Fact]
    public async Task BrokenUploadsAre400AndOnlyOneFileIsAccepted()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var username = "owner-" + Guid.NewGuid().ToString("N")[..6];
        var owner = await host.CreateSignedInClientAsync(username);

        using (var truncated = await SendRawAsync(host, owner, "--b1\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.txt\"\r\n\r\nhalf a file without its end"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, truncated.StatusCode);
            Assert.Contains("The upload could not be read.", await truncated.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        using (var noBoundary = await SendRawAsync(host, owner, "this is not multipart at all"))
            Assert.Equal(HttpStatusCode.BadRequest, noBoundary.StatusCode);

        using (var noFile = await SendRawAsync(host, owner, "--b1\r\nContent-Disposition: form-data; name=\"note\"\r\n\r\njust a field\r\n--b1--\r\n"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noFile.StatusCode);
            Assert.Contains("Send exactly one file per upload.", await noFile.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var two = $"--b1\r\nContent-Disposition: form-data; name=\"file\"; filename=\"one.txt\"\r\n\r\nFirst {Guid.NewGuid()}\r\n" +
                  $"--b1\r\nContent-Disposition: form-data; name=\"file\"; filename=\"two.txt\"\r\n\r\nSecond {Guid.NewGuid()}\r\n--b1--\r\n";
        using (var twoFiles = await SendRawAsync(host, owner, two))
            Assert.Equal(HttpStatusCode.BadRequest, twoFiles.StatusCode);

        var withField = $"--b1\r\nContent-Disposition: form-data; name=\"note\"\r\n\r\nignored\r\n" +
                        $"--b1\r\nContent-Disposition: form-data; name=\"file\"; filename=\"kept.txt\"\r\n\r\nKept {Guid.NewGuid()}\r\n--b1--\r\n";
        using (var kept = await SendRawAsync(host, owner, withField))
            Assert.Equal(HttpStatusCode.Created, kept.StatusCode);

        Assert.Equal(["kept.txt"], await host.QueryAsync(db => db.Sources.Where(s => s.OwnerUsername == username).Select(s => s.FileName).ToListAsync()));
        Assert.Equal(0, await IncomingCountAsync(host));
    }

    private static async Task<HttpResponseMessage> SendRawAsync(AtelierEndToEndHost host, HttpClient client, string body)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=b1");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sources") { Content = content };
        request.Headers.Add(AtelierEndToEndHost.AntiforgeryHeader, await host.GetAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendStalledAsync(AtelierEndToEndHost host, HttpClient client, string fileName, Task release)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sources") { Content = new StalledContent(fileName, release) };
        request.Headers.Add(AtelierEndToEndHost.AntiforgeryHeader, await host.GetAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    private static async Task WaitForIncomingAsync(AtelierEndToEndHost host, int count)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (await IncomingCountAsync(host) < count)
        {
            Assert.True(DateTime.UtcNow < deadline, $"fewer than {count} uploads started");
            await Task.Delay(50);
        }
    }

    private static Task<int> IncomingCountAsync(AtelierEndToEndHost host)
    {
        var root = host.Services.GetRequiredService<IOptions<SourceOptions>>().Value.StorageRoot;
        var incoming = Path.Combine(root, IncomingUpload.IncomingFolder);
        return Task.FromResult(Directory.Exists(incoming) ? Directory.GetDirectories(incoming).Length : 0);
    }

    /// <summary>Sends the head of a multipart upload and part of the file, then waits for <c>release</c> before it ends.</summary>
    private sealed class StalledContent : HttpContent
    {
        private const string Boundary = "stalled-boundary";
        private readonly string _fileName;
        private readonly Task _release;

        public StalledContent(string fileName, Task release)
        {
            _fileName = fileName;
            _release = release;
            Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={Boundary}");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{_fileName}\"\r\nContent-Type: text/plain\r\n\r\n"));
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"The first part of {_fileName} {Guid.NewGuid()} "));
            await stream.FlushAsync();
            await _release;
            await stream.WriteAsync(Encoding.UTF8.GetBytes("and the rest of it."));
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"\r\n--{Boundary}--\r\n"));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
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
