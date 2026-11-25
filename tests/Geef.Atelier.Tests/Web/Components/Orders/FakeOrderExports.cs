using System.Collections.Concurrent;
using System.Text;
using Geef.Atelier.Application.Orders;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>In-memory <see cref="IOrderExportService"/> for page tests: Markdown of the texts set per order and version; every call is recorded.</summary>
internal sealed class FakeOrderExports : IOrderExportService
{
    public ConcurrentDictionary<(Guid OrderId, int Version), string> Texts { get; } = new();

    public List<(Guid OrderId, int Version, string? User)> Calls { get; } = [];

    public Task<OrderExport?> ExportAsync(Guid orderId, ExportFormat format, int? version, string? requestingUsername, CancellationToken ct = default)
    {
        Assert.Equal(ExportFormat.Markdown, format);
        lock (Calls)
            Calls.Add((orderId, version!.Value, requestingUsername));
        return Task.FromResult(Texts.TryGetValue((orderId, version!.Value), out var text)
            ? new OrderExport(Encoding.UTF8.GetBytes(text), "text/markdown", "text.md")
            : null);
    }
}
