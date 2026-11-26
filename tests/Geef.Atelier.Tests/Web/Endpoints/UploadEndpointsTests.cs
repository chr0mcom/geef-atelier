using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.Web.Components.Orders;
using Geef.Atelier.Tests.Web.Uploads;
using Geef.Atelier.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Web.Endpoints;

/// <summary>
/// <c>PUT /api/uploads/{id}</c>, <c>POST /api/uploads/{id}/page</c> and <c>GET /u/{id}</c> over the real <c>Program</c>
/// wiring (routing, cookie sign-in, antiforgery, limits). The ticket service of U1 is replaced by <see cref="FakeUploadTickets"/>,
/// so these tests prove what the web layer lets through and how it answers; the same flows against the real service are in
/// <c>UploadTicketEndToEndTests</c>.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class UploadEndpointsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task TheTokenRouteTakesOnlyItsHeaderAndAnswersWithTheReceiptTheHashAndTheNextStep()
    {
        var tickets = new FakeUploadTickets();
        await using var host = await StartAsync(tickets);
        var owner = await host.CreateSignedInClientAsync("owner-" + Guid.NewGuid().ToString("N")[..6]);
        var id = tickets.Add("someone", "notes.md");
        var token = tickets.TokenOf(id);
        var bytes = Encoding.UTF8.GetBytes("# Notes\nThe delivery moves by two weeks. " + Guid.NewGuid());

        using (var cookieOnly = await PutAsync(owner, id, bytes, token: null))
            await AssertReasonAsync(cookieOnly, HttpStatusCode.NotFound, UploadTicketLines.LinkNotValid);
        using (var anonymous = host.CreateAnonymousClient())
        {
            using (var inQuery = await anonymous.PutAsync($"/api/uploads/{id}?token={token}", new ByteArrayContent(bytes)))
                await AssertReasonAsync(inQuery, HttpStatusCode.NotFound, UploadTicketLines.LinkNotValid);
            using (var tooLong = await PutAsync(anonymous, id, bytes, new string('a', UploadEndpoints.MaxTokenLength + 1)))
                await AssertReasonAsync(tooLong, HttpStatusCode.NotFound, UploadTicketLines.LinkNotValid);
            Assert.Empty(tickets.TokenUploads);

            using (var twice = new HttpRequestMessage(HttpMethod.Put, $"/api/uploads/{id}") { Content = new ByteArrayContent(bytes) })
            {
                twice.Headers.TryAddWithoutValidation(UploadTicketRoutes.TokenHeader, [token, token]);
                await AssertReasonAsync(await anonymous.SendAsync(twice), HttpStatusCode.NotFound, UploadTicketLines.LinkNotValid);
            }
            using (var wrong = await PutAsync(anonymous, id, bytes, "wrong-" + token))
                await AssertReasonAsync(wrong, HttpStatusCode.NotFound, UploadTicketLines.LinkNotValid);

            using var created = await PutAsync(anonymous, id, bytes, token, contentType: "text/html");
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            var root = body.RootElement;
            Assert.True(root.GetProperty("usable").GetBoolean());
            Assert.Equal("markdown", root.GetProperty("kind").GetString());
            Assert.Equal(id, root.GetProperty("uploadId").GetGuid());
            Assert.Equal(FakeUploadTickets.Sha256Of(bytes), root.GetProperty("sha256").GetString());
            Assert.Equal(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.AfterUploadFormat, id), root.GetProperty("next").GetString());
            Assert.False(root.TryGetProperty("refusalCode", out _));
            Assert.False(root.TryGetProperty("preview", out _));
            Assert.False(root.TryGetProperty("fileName", out _));
            var received = tickets.TokenUploads.Last();
            Assert.Equal(token, received.Token);
            Assert.Equal(bytes, received.Bytes);

            using (var again = await PutAsync(anonymous, id, bytes, token))
                await AssertReasonAsync(again, HttpStatusCode.NotFound, UploadTicketLines.LinkNotValid);
            using (var unknown = await PutAsync(anonymous, Guid.NewGuid(), bytes, token))
                await AssertReasonAsync(unknown, HttpStatusCode.NotFound, UploadTicketLines.LinkNotValid);
        }
    }

    [Fact]
    public async Task AnswersOfTheServiceKeepTheirStatusReasonAndReceipt()
    {
        var tickets = new FakeUploadTickets();
        var next = new ConcurrentQueue<UploadOutcome>();
        tickets.Outcome = (_, _) => next.TryDequeue(out var outcome) ? outcome : null;
        await using var host = await StartAsync(tickets);
        using var client = host.CreateAnonymousClient();
        var id = tickets.Add("someone", "scan.pdf");
        var token = tickets.TokenOf(id);
        var scanReason = "This PDF has no readable text. It may be a scan; scanned files are not supported yet.";

        next.Enqueue(new UploadOutcome(422, OrderReceipts.Unusable("scan.pdf", scanReason) with { RefusalCode = SourceRefusalCodes.PdfNoText }, "ab12", null));
        using (var unusable = await PutAsync(client, id, [1, 2, 3], token))
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, unusable.StatusCode);
            using var body = JsonDocument.Parse(await unusable.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("usable").GetBoolean());
            Assert.Equal(scanReason, body.RootElement.GetProperty("reason").GetString());
            Assert.Equal(id, body.RootElement.GetProperty("uploadId").GetGuid());
            Assert.Equal(UploadTicketLines.AfterRefusal, body.RootElement.GetProperty("next").GetString());
            Assert.False(body.RootElement.TryGetProperty("fileName", out _));
        }

        next.Enqueue(new UploadOutcome(409, null, null, null));
        using (var running = await PutAsync(client, id, [1], token))
            await AssertReasonAsync(running, HttpStatusCode.Conflict, UploadTicketLines.AlreadyRunning);

        var mismatch = string.Format(CultureInfo.InvariantCulture, UploadTicketLines.SizeMismatchFormat, 3, 10);
        next.Enqueue(new UploadOutcome(422, null, null, mismatch));
        using (var wrongSize = await PutAsync(client, id, [1, 2, 3], token))
            await AssertReasonAsync(wrongSize, HttpStatusCode.UnprocessableEntity, mismatch);

        next.Enqueue(new UploadOutcome(413, null, null, "The file is larger than 25 MB."));
        using (var tooLarge = await PutAsync(client, id, [1], token))
            await AssertReasonAsync(tooLarge, HttpStatusCode.RequestEntityTooLarge, "The file is larger than 25 MB.");

        next.Enqueue(new UploadOutcome(503, OrderReceipts.Unusable("scan.pdf", "All slots are busy.") with { RefusalCode = SourceRefusalCodes.Busy }, null, null));
        using (var busy = await PutAsync(client, id, [1], token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
            Assert.Equal(SourceEndpoints.BusyRetryAfterSeconds, busy.Headers.GetValues("Retry-After").Single());
            using var body = JsonDocument.Parse(await busy.Content.ReadAsStringAsync());
            Assert.Equal(UploadTicketLines.AfterNotChecked, body.RootElement.GetProperty("next").GetString());
        }

        foreach (var (code, expected) in new[] { (SourceRefusalCodes.Quota, UploadTicketLines.AfterQuota), (SourceRefusalCodes.Unavailable, UploadTicketLines.AfterUnavailable) })
        {
            next.Enqueue(new UploadOutcome(422, OrderReceipts.Unusable("scan.pdf", "Not now.") with { RefusalCode = code }, null, null));
            using var waiting = await PutAsync(client, id, [1], token);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, waiting.StatusCode);
            using var body = JsonDocument.Parse(await waiting.Content.ReadAsStringAsync());
            var said = body.RootElement.GetProperty("next").GetString()!;
            Assert.Equal(expected, said);
            Assert.Contains("running curlCommand again now will not help", said, StringComparison.Ordinal);
            Assert.DoesNotContain("then run curlCommand again", said, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RequestsAboveTheLimitAre413BeforeAnythingIsReadAndCountedWhenThePersonIsKnown()
    {
        var tickets = new FakeUploadTickets();
        var refusals = new RecordingRefusalLog();
        await using var host = await StartAsync(tickets, refusals);
        var username = "big-" + Guid.NewGuid().ToString("N")[..6];
        var owner = await host.CreateSignedInClientAsync(username);
        var id = tickets.Add(username, "huge.pdf");
        var huge = new byte[SourceEndpoints.RequestLimitBytes + 1];

        using (var anonymous = host.CreateAnonymousClient())
        using (var put = await PutAsync(anonymous, id, huge, tickets.TokenOf(id)))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, put.StatusCode);
        Assert.Empty(tickets.TokenUploads);
        Assert.Empty(refusals.Records);

        using (var page = await PostPageAsync(host, owner, id, "huge.pdf", huge))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, page.StatusCode);
        Assert.Empty(tickets.PageUploads);
        var counted = Assert.Single(refusals.Records);
        Assert.Equal(new SourceRefusalRecord(username, RefusalPath.UploadPage, SourceRefusalCodes.TooLarge, null, null, null, counted.SizeBytes), counted);
        Assert.True(counted.SizeBytes > SourceEndpoints.RequestLimitBytes);

        using (var web = await host.PostSourceAsync(owner, "huge.txt", huge))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, web.StatusCode);
        Assert.Equal(RefusalPath.Web, refusals.Records.Last().Path);
        Assert.Equal(2, refusals.Records.Count);

        using (var forged = await host.PostSourceAsync(owner, "huge.txt", huge, withAntiforgery: false))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, forged.StatusCode);
        Assert.Equal(2, refusals.Records.Count);
    }

    [Fact]
    public async Task WithTheSwitchOffTheRoutesAndThePageAreNotFound()
    {
        var tickets = new FakeUploadTickets { Enabled = false };
        await using var host = await StartAsync(tickets);
        var username = "off-" + Guid.NewGuid().ToString("N")[..6];
        var owner = await host.CreateSignedInClientAsync(username);
        var id = tickets.Add(username, "Plan for the switch.md");

        using (var anonymous = host.CreateAnonymousClient())
        using (var put = await PutAsync(anonymous, id, [1, 2], tickets.TokenOf(id)))
        using (var unknown = await anonymous.PutAsync($"/api/uploads-unknown/{id}", new ByteArrayContent([1, 2])))
            Assert.Equal(unknown.StatusCode, put.StatusCode);
        using (var page = await PostPageAsync(host, owner, id, "plan.md", [1, 2]))
        using (var unknownPage = await owner.PostAsync($"/api/uploads-unknown/{id}/page", new ByteArrayContent([1])))
            Assert.Equal(unknownPage.StatusCode, page.StatusCode);

        using var view = await owner.GetAsync($"/u/{id}");
        Assert.Equal(HttpStatusCode.NotFound, view.StatusCode);
        Assert.DoesNotContain("Plan for the switch", await view.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(tickets.TokenUploads);
        Assert.Empty(tickets.PageUploads);
        Assert.Empty(tickets.Probes);
    }

    [Fact]
    public async Task ThePageUploadNeedsTheSignedInOwnerTheAntiforgeryHeaderAndTheSameSite()
    {
        var tickets = new FakeUploadTickets();
        await using var host = await StartAsync(tickets);
        var username = "page-" + Guid.NewGuid().ToString("N")[..6];
        var owner = await host.CreateSignedInClientAsync(username);
        var stranger = await host.CreateSignedInClientAsync("stranger-" + Guid.NewGuid().ToString("N")[..6]);
        var admin = await host.CreateSignedInClientAsync("admin-" + Guid.NewGuid().ToString("N")[..6], isAdmin: true);
        var nameless = await host.CreateSignedInClientAsync("nameless-" + Guid.NewGuid().ToString("N")[..6], withName: false);
        var id = tickets.Add(username, "letter.docx");
        var bytes = Encoding.UTF8.GetBytes("Letter text " + Guid.NewGuid());

        using (var anonymous = host.CreateAnonymousClient())
        using (var post = await anonymous.PostAsync($"/api/uploads/{id}/page", new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", "a.md" } }))
            Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
        using (var forged = await PostPageAsync(host, owner, id, "a.md", bytes, withAntiforgery: false))
            await AssertReasonAsync(forged, HttpStatusCode.BadRequest, SourceEndpoints.OutOfDateReason);
        using (var crossSite = await PostPageAsync(host, owner, id, "a.md", bytes, origin: "https://evil.example"))
            Assert.Equal(HttpStatusCode.BadRequest, crossSite.StatusCode);
        using (var foreign = await PostPageAsync(host, stranger, id, "a.md", bytes))
            await AssertReasonAsync(foreign, HttpStatusCode.NotFound, UploadEndpoints.PageNotFoundReason);
        using (var asAdmin = await PostPageAsync(host, admin, id, "a.md", bytes))
            await AssertReasonAsync(asAdmin, HttpStatusCode.NotFound, UploadEndpoints.PageNotFoundReason);
        using (var noName = await PostPageAsync(host, nameless, id, "a.md", bytes))
            await AssertReasonAsync(noName, HttpStatusCode.Forbidden, OwnerScope.UnknownUserMessage);
        Assert.Empty(tickets.PageUploads);
        Assert.Equal(UploadClaimProbe.Open, tickets.StateOf(id));

        using (var created = await PostPageAsync(host, owner, id, "Letter to the landlord.md", bytes))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
            Assert.True(body.RootElement.GetProperty("usable").GetBoolean());
            Assert.Equal(id, body.RootElement.GetProperty("uploadId").GetGuid());
            Assert.Equal(FakeUploadTickets.Sha256Of(bytes), body.RootElement.GetProperty("sha256").GetString());
            Assert.False(body.RootElement.TryGetProperty("next", out _));
        }
        var received = Assert.Single(tickets.PageUploads);
        Assert.Equal((id, username, "Letter to the landlord.md"), (received.Id, received.Owner, received.FileName));
        Assert.Equal(bytes, received.Bytes);

        using (var again = await PostPageAsync(host, owner, id, "a.md", bytes))
            await AssertReasonAsync(again, HttpStatusCode.Conflict, UploadTicketLines.PageAlreadyUsed);
        var expired = tickets.Add(username, "old.md", UploadClaimProbe.Expired);
        using (var gone = await PostPageAsync(host, owner, expired, "a.md", bytes))
            await AssertReasonAsync(gone, HttpStatusCode.Gone, UploadTicketLines.PageExpired);
        var failed = tickets.Add(username, "refused.md", UploadClaimProbe.Failed);
        using (var refused = await PostPageAsync(host, owner, failed, "a.md", bytes))
            await AssertReasonAsync(refused, HttpStatusCode.Gone, UploadTicketLines.PageFailed);
        var running = tickets.Add(username, "busy.md", UploadClaimProbe.Receiving);
        using (var busy = await PostPageAsync(host, owner, running, "a.md", bytes))
            await AssertReasonAsync(busy, HttpStatusCode.Conflict, UploadTicketLines.AlreadyRunning);
        Assert.Single(tickets.PageUploads);
    }

    [Fact]
    public async Task WhenTheClaimFailsDuringThePageUploadTheAnswerTellsTheStateOfTheLink()
    {
        var tickets = new FakeUploadTickets();
        await using var host = await StartAsync(tickets);
        var username = "race-" + Guid.NewGuid().ToString("N")[..6];
        var owner = await host.CreateSignedInClientAsync(username);
        var id = tickets.Add(username, "notes.md");
        tickets.Outcome = (ticket, _) =>
        {
            tickets.Set(ticket, UploadClaimProbe.Used);
            return new UploadOutcome(404, null, null, null);
        };

        using var raced = await PostPageAsync(host, owner, id, "notes.md", [1, 2, 3]);

        await AssertReasonAsync(raced, HttpStatusCode.Conflict, UploadTicketLines.PageAlreadyUsed);
    }

    [Fact]
    public async Task ThePageAsksForTheSignInAndShowsAnotherPersonOnlyNotFound()
    {
        var tickets = new FakeUploadTickets();
        await using var host = await StartAsync(tickets);
        var username = "show-" + Guid.NewGuid().ToString("N")[..6];
        var owner = await host.CreateSignedInClientAsync(username);
        var stranger = await host.CreateSignedInClientAsync("stranger-" + Guid.NewGuid().ToString("N")[..6]);
        var id = tickets.Add(username, "Offer for the garden.pdf");

        using (var anonymous = host.CreateAnonymousClient())
        using (var page = await anonymous.GetAsync($"/u/{id}"))
        {
            Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
            Assert.StartsWith("/login", page.Headers.Location!.IsAbsoluteUri ? page.Headers.Location.PathAndQuery : page.Headers.Location.OriginalString, StringComparison.Ordinal);
        }
        Assert.Empty(tickets.Probes);

        var foreign = await stranger.GetStringAsync($"/u/{id}");
        Assert.Contains("Not Found", foreign, StringComparison.Ordinal);
        Assert.DoesNotContain("Offer for the garden", foreign, StringComparison.Ordinal);

        var own = await owner.GetStringAsync($"/u/{id}");
        Assert.Contains(UploadTicketLines.PageHeading, own, StringComparison.Ordinal);
        Assert.Contains("Offer for the garden.pdf", own, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTokenNeverAppearsInALog()
    {
        var tickets = new FakeUploadTickets();
        var logs = new CapturingLoggerProvider();
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            Register(services, tickets, null);
            services.AddLogging(b =>
            {
                b.AddProvider(logs);
                b.AddFilter<CapturingLoggerProvider>(null, LogLevel.Trace);
            });
        });
        using var client = host.CreateAnonymousClient();
        var id = tickets.Add("someone", "notes.md");
        var token = tickets.TokenOf(id);

        using (var wrong = await PutAsync(client, id, [1, 2], token + "x"))
            Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using (var created = await PutAsync(client, id, Encoding.UTF8.GetBytes("Notes"), token))
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Assert.NotEmpty(logs.Messages);
        Assert.Contains(logs.Messages, m => m.Contains($"/api/uploads/{id}", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Messages, m => m.Contains(token, StringComparison.Ordinal));
    }

    private Task<AtelierEndToEndHost> StartAsync(FakeUploadTickets tickets, ISourceRefusalLog? refusals = null) =>
        AtelierEndToEndHost.StartAsync(postgres, services => Register(services, tickets, refusals));

    private static void Register(IServiceCollection services, FakeUploadTickets tickets, ISourceRefusalLog? refusals)
    {
        services.AddSingleton<IUploadTicketService>(tickets);
        services.AddSingleton<IUploadTicketRepository>(tickets);
        if (refusals is not null)
            services.AddSingleton(refusals);
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, byte[] bytes, string? token, string? contentType = null)
    {
        var content = new ByteArrayContent(bytes);
        if (contentType is not null)
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/uploads/{id}") { Content = content };
        if (token is not null)
            request.Headers.Add(UploadTicketRoutes.TokenHeader, token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostPageAsync(AtelierEndToEndHost host, HttpClient client, Guid id, string fileName, byte[] bytes, bool withAntiforgery = true, string? origin = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/uploads/{id}/page") { Content = form };
        if (withAntiforgery)
            request.Headers.Add(AtelierEndToEndHost.AntiforgeryHeader, await host.GetAntiforgeryTokenAsync(client));
        if (origin is not null)
            request.Headers.Add("Origin", origin);
        return await client.SendAsync(request);
    }

    private static async Task AssertReasonAsync(HttpResponseMessage response, HttpStatusCode status, string reason)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(status == response.StatusCode, $"Expected {(int)status}, got {(int)response.StatusCode}: {raw}");
        using var body = JsonDocument.Parse(raw);
        Assert.Equal(reason, body.RootElement.GetProperty("reason").GetString());
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(this);

        public void Dispose()
        {
        }

        private sealed class Logger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                provider.Messages.Enqueue(formatter(state, exception));
                if (exception is not null)
                    provider.Messages.Enqueue(exception.ToString());
            }
        }
    }
}
