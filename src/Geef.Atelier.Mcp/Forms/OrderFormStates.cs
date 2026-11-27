using System.Security.Cryptography;
using System.Text.Json;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Mcp.Forms;

/// <summary>
/// What <c>submit_order</c> needs when the client comes back with the answers of its form (D-088). It travels as <c>requestState</c> through the
/// client, protected so that the client can neither read nor change it.
/// </summary>
/// <param name="OrderId">The order the form belongs to; no second order is created when the answers come back.</param>
/// <param name="Username">The user who created the order; only this user can answer it.</param>
/// <param name="ClientId">The client the user signed in with (the OAuth client, or the user name for other tokens).</param>
/// <param name="Arguments">Hash of the arguments of the first call (<see cref="OrderFormStates.HashArguments"/>); the client must repeat them.</param>
/// <param name="Questions">Ids of the questions of the form.</param>
/// <param name="ExpiresAt">When the answers can no longer come back through the form.</param>
/// <param name="Sources">The receipts of the files of the first call, which the client has not seen yet.</param>
/// <param name="Notices">The notices of the first call, which the client has not seen yet.</param>
internal sealed record OrderFormState(
    Guid OrderId,
    string Username,
    string? ClientId,
    string Arguments,
    IReadOnlyList<string> Questions,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<OrderSourceDto> Sources,
    IReadOnlyList<string> Notices);

/// <summary>Whether a <c>requestState</c> came from this server and is still valid.</summary>
internal enum OrderFormStateCheck
{
    /// <summary>Made by this server, unchanged and not expired.</summary>
    Valid = 0,

    /// <summary>Made by this server and unchanged, but older than <see cref="McpOptions.ElicitationLifetime"/>.</summary>
    Expired = 1,

    /// <summary>Not made by this server, changed, unreadable or made with a key this server no longer has.</summary>
    Invalid = 2,
}

/// <summary>Protects and reads the <c>requestState</c> of the form of <c>submit_order</c>.</summary>
internal interface IOrderFormStates
{
    /// <summary>The state for an order whose form is sent now; it expires after <see cref="McpOptions.ElicitationLifetime"/>.</summary>
    OrderFormState Create(Guid orderId, string username, string? clientId, string arguments, IReadOnlyList<string> questions,
        IReadOnlyList<OrderSourceDto> sources, IReadOnlyList<string> notices);

    /// <summary>The protected text of <paramref name="state"/>, signed and encrypted with the data protection keys of the server.</summary>
    string Protect(OrderFormState state);

    /// <summary>The state in <paramref name="protectedState"/> and whether it is valid; the state is <c>null</c> only when it is <see cref="OrderFormStateCheck.Invalid"/>.</summary>
    (OrderFormStateCheck Check, OrderFormState? State) Read(string? protectedState);
}

/// <inheritdoc />
internal sealed class OrderFormStates(IDataProtectionProvider protection, TimeProvider time, IOptions<McpOptions> options) : IOrderFormStates
{
    /// <summary>Purpose of the data protection keys; a state made for another purpose cannot be read.</summary>
    public const string Purpose = "Geef.Atelier.Mcp.OrderForm.v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>A stable hash of the arguments of <c>submit_order</c>, so that the answers come back only for the same call.</summary>
    public static string HashArguments(string request, string? attachments, bool extraCareful, IReadOnlyList<string>? uploadIds) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new object?[] { request, attachments, extraCareful, uploadIds ?? [] }, JsonOptions)));

    public OrderFormState Create(Guid orderId, string username, string? clientId, string arguments, IReadOnlyList<string> questions,
        IReadOnlyList<OrderSourceDto> sources, IReadOnlyList<string> notices) =>
        new(orderId, username, clientId, arguments, questions, time.GetUtcNow() + options.Value.ElicitationLifetime, sources, notices);

    public string Protect(OrderFormState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return _protector.Protect(JsonSerializer.Serialize(state, JsonOptions));
    }

    public (OrderFormStateCheck Check, OrderFormState? State) Read(string? protectedState)
    {
        if (string.IsNullOrWhiteSpace(protectedState))
            return (OrderFormStateCheck.Invalid, null);

        OrderFormState? state;
        try
        {
            state = JsonSerializer.Deserialize<OrderFormState>(_protector.Unprotect(protectedState), JsonOptions);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or NotSupportedException)
        {
            return (OrderFormStateCheck.Invalid, null);
        }

        if (state is null || string.IsNullOrEmpty(state.Username) || state.Questions is null || state.Sources is null || state.Notices is null)
            return (OrderFormStateCheck.Invalid, null);
        return (time.GetUtcNow() >= state.ExpiresAt ? OrderFormStateCheck.Expired : OrderFormStateCheck.Valid, state);
    }
}
