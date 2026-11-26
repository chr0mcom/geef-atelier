using System.Collections.Concurrent;
using System.Security.Cryptography;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Tests.Web.Components.Orders;

namespace Geef.Atelier.Tests.Web.Uploads;

/// <summary>
/// In-memory stand-in for the ticket service of U1 and the owner lookup of its repository. Tickets are added by the test;
/// a token or page upload of an open ticket answers 201 with a usable receipt and the SHA-256 of the received bytes, unless
/// <see cref="Outcome"/> decides otherwise. Every call is recorded, so tests can prove what reached the service.
/// </summary>
internal sealed class FakeUploadTickets : IUploadTicketService, IUploadTicketRepository
{
    private readonly ConcurrentDictionary<Guid, Ticket> _tickets = new();

    public bool Enabled { get; set; } = true;

    public Func<Guid, byte[], UploadOutcome?>? Outcome { get; set; }

    public Exception? ProbeError { get; set; }

    public ConcurrentQueue<(Guid Id, string Token, byte[] Bytes)> TokenUploads { get; } = new();

    public ConcurrentQueue<(Guid Id, string Owner, string FileName, byte[] Bytes)> PageUploads { get; } = new();

    public ConcurrentQueue<(Guid Id, string Owner)> Probes { get; } = new();

    public Guid Add(string owner, string fileName, UploadClaimProbe state = UploadClaimProbe.Open, string? token = null)
    {
        var id = Guid.NewGuid();
        _tickets[id] = new Ticket(owner, fileName, token ?? "token-" + id.ToString("N"), state);
        return id;
    }

    public string TokenOf(Guid id) => _tickets[id].Token;

    public void Set(Guid id, UploadClaimProbe state) => _tickets[id] = _tickets[id] with { State = state };

    public UploadClaimProbe StateOf(Guid id) => _tickets[id].State;

    public Task<PreparedUpload> PrepareAsync(string owner, string fileName, long? sizeBytes, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public async Task<UploadOutcome> ReceiveByTokenAsync(Guid uploadId, string token, Stream body, CancellationToken ct = default)
    {
        var bytes = await ReadAsync(body, ct);
        TokenUploads.Enqueue((uploadId, token, bytes));
        if (!_tickets.TryGetValue(uploadId, out var ticket) || ticket.Token != token || ticket.State != UploadClaimProbe.Open)
            return new UploadOutcome(404, null, null, UploadTicketLines.LinkNotValid);
        return Complete(uploadId, ticket.FileName, bytes);
    }

    public async Task<UploadOutcome> ReceiveFromPageAsync(Guid uploadId, string owner, string fileName, Stream body, CancellationToken ct = default)
    {
        var bytes = await ReadAsync(body, ct);
        PageUploads.Enqueue((uploadId, owner, fileName, bytes));
        if (!_tickets.TryGetValue(uploadId, out var ticket) || ticket.Owner != owner || ticket.State != UploadClaimProbe.Open)
            return new UploadOutcome(404, null, null, null);
        return Complete(uploadId, fileName, bytes);
    }

    public Task<UploadClaimProbe> ProbeAsync(Guid uploadId, string owner, CancellationToken ct = default)
    {
        Probes.Enqueue((uploadId, owner));
        if (ProbeError is { } error)
            return Task.FromException<UploadClaimProbe>(error);
        return Task.FromResult(_tickets.TryGetValue(uploadId, out var ticket) && ticket.Owner == owner ? ticket.State : UploadClaimProbe.NotFound);
    }

    public Task<IReadOnlyList<SourceReceipt>> ResolveAsync(IReadOnlyList<Guid> uploadIds, string owner, Func<Guid, string> pageLink, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<UploadTicketEntity>> FindForOwnerAsync(IReadOnlyCollection<Guid> ids, string owner, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UploadTicketEntity>>(ids
            .Where(id => _tickets.TryGetValue(id, out var t) && t.Owner == owner)
            .Select(id => Entity(id, _tickets[id]))
            .ToList());

    public Task AddAsync(UploadTicketEntity ticket, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<bool> AddWithinLimitsAsync(UploadTicketEntity ticket, int maxOpen, int maxPerDay, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<(int Open, int Last24h)> CountForOwnerAsync(string owner, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<UploadTicketEntity?> TryClaimByTokenAsync(Guid id, string tokenHash, DateTimeOffset now, int maxAttempts, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<UploadTicketEntity?> TryClaimByOwnerAsync(Guid id, string owner, DateTimeOffset now, int maxAttempts, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<UploadClaimProbe> ProbeAsync(Guid id, string owner, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<UploadClaimProbe> ProbeByTokenAsync(Guid id, string tokenHash, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<bool> CompleteAsync(Guid id, DateTimeOffset receivingSince, Guid sourceId, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();

    public Task ReleaseAsync(Guid id, DateTimeOffset receivingSince, int maxAttempts, string? checkedRefusalCode, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<int> ResetStaleReceivingAsync(DateTimeOffset olderThan, int maxAttempts, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<bool> ResetStaleReceivingAsync(Guid id, DateTimeOffset olderThan, int maxAttempts, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<int> DeleteExpiredAsync(DateTimeOffset before, CancellationToken ct = default) => throw new NotSupportedException();

    public static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private UploadOutcome Complete(Guid id, string fileName, byte[] bytes)
    {
        if (Outcome?.Invoke(id, bytes) is { } decided)
            return decided;
        Set(id, UploadClaimProbe.Used);
        return new UploadOutcome(201, OrderReceipts.Usable(fileName, chars: bytes.Length, size: bytes.Length), Sha256Of(bytes), null);
    }

    private static async Task<byte[]> ReadAsync(Stream body, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await body.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private static UploadTicketEntity Entity(Guid id, Ticket ticket) => new()
    {
        Id = id,
        OwnerUsername = ticket.Owner,
        FileName = ticket.FileName,
        TokenHash = new string('0', 64),
        State = ticket.State switch
        {
            UploadClaimProbe.Used => UploadTicketState.Used,
            UploadClaimProbe.Receiving => UploadTicketState.Receiving,
            UploadClaimProbe.Expired or UploadClaimProbe.Failed => UploadTicketState.Failed,
            _ => UploadTicketState.Open,
        },
        CreatedAt = DateTimeOffset.UtcNow,
        TokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(24),
    };

    private sealed record Ticket(string Owner, string FileName, string Token, UploadClaimProbe State);
}

/// <summary>Records counted refusals instead of storing them.</summary>
internal sealed class RecordingRefusalLog : ISourceRefusalLog
{
    public ConcurrentQueue<SourceRefusalRecord> Records { get; } = new();

    public Task RecordAsync(SourceRefusalRecord record, CancellationToken ct = default)
    {
        Records.Enqueue(record);
        return Task.CompletedTask;
    }
}
