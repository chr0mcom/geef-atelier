using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.EndToEnd;

internal sealed partial class AtelierEndToEndHost
{
    /// <summary>Name of the auth cookie set in <c>Program</c>.</summary>
    public const string AuthCookieName = "Atelier.Auth";

    /// <summary>Header that carries the antiforgery request token.</summary>
    public const string AntiforgeryHeader = "RequestVerificationToken";

    private static readonly ConditionalWeakTable<HttpClient, SignedInSession> Sessions = new();

    /// <summary>
    /// Creates a user and returns an HTTP client signed in with the auth cookie of that user. The client does not
    /// follow redirects, so a challenge shows up as 302 or 401. With <paramref name="withName"/> false the cookie carries
    /// no user name (a broken sign-in), to check that owned data is then refused.
    /// </summary>
    public async Task<HttpClient> CreateSignedInClientAsync(string username, bool isAdmin = false, bool withName = true)
    {
        var userId = await EnsureUserAsync(username, isAdmin);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (withName)
            claims.Add(new Claim(ClaimTypes.Name, username));
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "admin"));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        var cookieOptions = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var now = DateTimeOffset.UtcNow;
        var ticket = new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = now.AddHours(1) },
            CookieAuthenticationDefaults.AuthenticationScheme);

        var client = CreateAnonymousClient();
        var session = new SignedInSession(principal);
        session.Cookies[AuthCookieName] = cookieOptions.TicketDataFormat.Protect(ticket);
        Sessions.AddOrUpdate(client, session);
        session.Apply(client);
        return client;
    }

    /// <summary>Returns a client without any cookie that does not follow redirects.</summary>
    public HttpClient CreateAnonymousClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    /// <summary>
    /// Fetches an antiforgery request token for <paramref name="client"/>, to be sent in the <c>RequestVerificationToken</c>
    /// header. The matching antiforgery cookie is added to the client.
    /// </summary>
    public async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        if (!Sessions.TryGetValue(client, out var session))
            throw new InvalidOperationException("The client was not created by CreateSignedInClientAsync.");
        if (session.RequestToken is { } cached)
            return cached;

        await using var scope = Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = session.Principal };
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        var tokens = scope.ServiceProvider.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);

        foreach (var header in context.Response.Headers.SetCookie)
        {
            var pair = header!.Split(';', 2)[0];
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            session.Cookies[pair[..equals]] = pair[(equals + 1)..];
        }

        session.RequestToken = tokens.RequestToken ?? throw new InvalidOperationException("No antiforgery request token was issued.");
        session.Apply(client);
        return session.RequestToken;
    }

    /// <summary>Uploads one file to <c>POST /api/sources</c> and returns the receipt JSON (also for unusable files, answered with 422).</summary>
    public async Task<JsonElement> UploadSourceAsync(HttpClient client, string fileName, byte[] content)
    {
        using var response = await PostSourceAsync(client, fileName, content);
        var raw = await response.Content.ReadAsStringAsync();
        if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity))
            throw new InvalidOperationException($"Upload of '{fileName}' failed with {(int)response.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    /// <summary>Posts one file as multipart form data with the antiforgery header and returns the raw response.</summary>
    public async Task<HttpResponseMessage> PostSourceAsync(HttpClient client, string fileName, byte[] content, bool withAntiforgery = true)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/sources") { Content = form };
        if (withAntiforgery)
            request.Headers.Add(AntiforgeryHeader, await GetAntiforgeryTokenAsync(client));
        return await client.SendAsync(request);
    }

    /// <summary>Submits an order through the real <see cref="IOrderService"/> and then releases the orchestrator.</summary>
    public async Task<OrderSubmission> SubmitOrderAsync(SubmitOrderRequest request)
    {
        await using var scope = Services.CreateAsyncScope();
        var submission = await scope.ServiceProvider.GetRequiredService<IOrderService>().SubmitAsync(request);
        ReleaseOrchestrator();
        return submission;
    }

    /// <summary>Waits until the order is finished and its run has an outcome; fails with the recorded LLM traffic on timeout.</summary>
    public async Task<OrderView> WaitForOrderFinishedAsync(Guid orderId, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(90));
        OrderView? view = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            view = await WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(orderId, null));
            if (view is { Display.IsFinished: true } && (view.CurrentRun is null || view.CurrentRun.Outcome is not null))
                return view;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException(
            $"Order {orderId} did not finish in time (last state: {view?.Display.State.ToString() ?? "missing"}).\n" +
            $"LLM traffic:\n{Llm.Describe()}");
    }

    private async Task<string> EnsureUserAsync(string username, bool isAdmin)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IAtelierUserRepository>();
        var existing = await users.FindByUsernameAsync(username, CancellationToken.None);
        if (existing is not null)
            return existing.UserId;

        var user = new AtelierUser(
            UserId: Guid.NewGuid().ToString(),
            Username: username,
            PasswordHash: BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), workFactor: 4),
            Email: null,
            IsActive: true,
            IsAdmin: isAdmin,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);
        await users.AddAsync(user, CancellationToken.None);
        return user.UserId;
    }

    private sealed class SignedInSession(ClaimsPrincipal principal)
    {
        public ClaimsPrincipal Principal { get; } = principal;
        public Dictionary<string, string> Cookies { get; } = new(StringComparer.Ordinal);
        public string? RequestToken { get; set; }

        public void Apply(HttpClient client)
        {
            client.DefaultRequestHeaders.Remove("Cookie");
            client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", Cookies.Select(c => $"{c.Key}={c.Value}")));
        }
    }
}
