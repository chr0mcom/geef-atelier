namespace Geef.Atelier.Tests.EndToEnd.Fakes;

/// <summary>
/// Primary HTTP handler that hands every request to a shared responder. A new instance is created per
/// handler rotation of <see cref="IHttpClientFactory"/>, so disposing it never tears down the shared fake.
/// </summary>
internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        respond(request, cancellationToken);
}
