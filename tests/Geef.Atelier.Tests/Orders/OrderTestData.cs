using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Tests.Orders;

/// <summary>Synthetic orders, runs and sources for order and source tests.</summary>
internal static class OrderTestData
{
    public static OrderEntity Order(string owner, string text = "Write a short note.", OrderState state = OrderState.Ready, DateTimeOffset? at = null)
    {
        var time = at ?? DateTimeOffset.UtcNow;
        return new OrderEntity
        {
            Id = Guid.NewGuid(), CreatedByUser = owner, RequestText = text, Mode = OrderMode.Direct, State = state,
            Entry = OrderEntry.Web, CreatedAt = time, UpdatedAt = time,
        };
    }

    public static RunEntity Run(
        string? owner, Guid? orderId = null, int? version = null, RunKind kind = RunKind.Standard,
        RunStatus status = RunStatus.Pending, DateTimeOffset? at = null, string? finalText = null,
        Guid? parentRunId = null, Guid? parentCompositionRunId = null, string briefing = "Write a short note.", string config = "{}") => new()
    {
        Id = Guid.NewGuid(), CreatedAt = at ?? DateTimeOffset.UtcNow, Status = status, BriefingText = briefing, ConfigJson = config,
        CreatedByUser = owner, Kind = kind, OrderId = orderId, OrderVersion = version, FinalText = finalText,
        ParentRunId = parentRunId, ParentCompositionRunId = parentCompositionRunId,
    };

    public static SourceEntity Source(string owner, string? sha = null, int chars = 1_000, string text = "Some text.", DateTimeOffset? expiresAt = null, string? storagePath = null, int? pages = null) => new()
    {
        Id = Guid.NewGuid(), OwnerUsername = owner, FileName = "notes.md", ContentType = "text/markdown", Kind = SourceKind.Markdown,
        SizeBytes = text.Length, Sha256 = sha ?? Convert.ToHexStringLower(Guid.NewGuid().ToByteArray()).PadRight(64, '0'),
        ExtractedText = text, CharCount = chars, WordCount = 2, Quality = SourceQuality.Good,
        Warnings = [new SourceWarning(SourceWarningCodes.PagesWithoutText, "Page 2 has no text.", [2])],
        Origin = SourceOrigin.WebUpload, ExpiresAt = expiresAt, StoragePath = storagePath, PageCount = pages, CreatedAt = DateTimeOffset.UtcNow,
    };

    public static OrderSourceEntity Link(Guid orderId, Guid sourceId, int position = 0, string? pages = null) => new()
    {
        OrderId = orderId, SourceId = sourceId, Role = SourceRole.Facts, Position = position, PageSelection = pages, AddedAt = DateTimeOffset.UtcNow,
    };

    public static string NewUser() => "user-" + Guid.NewGuid().ToString("N")[..10];
}
