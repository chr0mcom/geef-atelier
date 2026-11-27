using System.Globalization;
using System.Text;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Orders.Export;

namespace Geef.Atelier.Infrastructure.Orders;

internal sealed class OrderExportService(IOrderRepository orders) : IOrderExportService
{
    public async Task<OrderExport?> ExportAsync(Guid orderId, ExportFormat format, int? version, string? requestingUsername, CancellationToken ct = default)
    {
        var order = await orders.GetAsync(orderId, ct);
        if (order is null || (requestingUsername is not null && order.CreatedByUser != requestingUsername))
            return null;

        var runs = await orders.ListRunsAsync(orderId, ct);
        var run = version is { } wanted
            ? runs.Where(r => r.Kind == RunKind.Standard && r.OrderVersion == wanted).OrderByDescending(r => r.CreatedAt).FirstOrDefault()
            : OrderStateResolver.SelectShownRun(runs);
        if (run is null || string.IsNullOrWhiteSpace(run.FinalText) || run.Kind != RunKind.Standard)
            return null;

        var title = order.RequestText.ToOrderTitle();
        var fileName = string.Create(CultureInfo.InvariantCulture, $"{title.ToFileSlug()}-v{run.OrderVersion ?? 1}.{format.ToRouteValue()}");
        return format switch
        {
            ExportFormat.Docx => new OrderExport(MarkdownToDocxConverter.Convert(run.FinalText),
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName),
            ExportFormat.Pdf => new OrderExport(MarkdownToPdfConverter.Convert(run.FinalText), "application/pdf", fileName),
            _ => new OrderExport(Encoding.UTF8.GetBytes(run.FinalText), "text/markdown; charset=utf-8", fileName),
        };
    }
}
