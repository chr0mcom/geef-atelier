namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// Returns a vanilla <see cref="HttpClient"/> with no real transport configured.
/// Sufficient for unit tests that only exercise code paths that never actually
/// fire an HTTP request (e.g. CLI provider connection-test short-circuit).
/// </summary>
internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}
