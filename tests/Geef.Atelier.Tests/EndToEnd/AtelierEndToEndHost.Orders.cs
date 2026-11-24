using System.Text.Json;
using Geef.Atelier.Application.Orders;

namespace Geef.Atelier.Tests.EndToEnd;

internal sealed partial class AtelierEndToEndHost
{
    /// <summary>Creates a user and returns an HTTP client signed in with the auth cookie of that user.</summary>
    public Task<HttpClient> CreateSignedInClientAsync(string username, bool isAdmin = false) =>
        throw new NotImplementedException();

    /// <summary>Fetches an antiforgery request token for <paramref name="client"/>, to be sent in the <c>RequestVerificationToken</c> header.</summary>
    public Task<string> GetAntiforgeryTokenAsync(HttpClient client) =>
        throw new NotImplementedException();

    /// <summary>Uploads one file to <c>POST /api/sources</c> and returns the receipt JSON.</summary>
    public Task<JsonElement> UploadSourceAsync(HttpClient client, string fileName, byte[] content) =>
        throw new NotImplementedException();

    /// <summary>Submits an order through the real <see cref="IOrderService"/> and then releases the orchestrator.</summary>
    public Task<OrderSubmission> SubmitOrderAsync(SubmitOrderRequest request) =>
        throw new NotImplementedException();

    /// <summary>Waits until the order is finished and returns its view; fails with the recorded LLM traffic on timeout.</summary>
    public Task<OrderView> WaitForOrderFinishedAsync(Guid orderId, TimeSpan? timeout = null) =>
        throw new NotImplementedException();
}
