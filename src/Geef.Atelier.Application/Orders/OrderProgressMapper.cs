using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Orders;

namespace Geef.Atelier.Application.Orders;

/// <summary>Maps the display state and the pipeline events of the current run to plain-English progress steps.</summary>
public static class OrderProgressMapper
{
    /// <summary>
    /// Builds the steps of the progress display: direct path <c>files</c> (only with sources), <c>write</c>, <c>check</c>,
    /// <c>fix</c> (only when a second iteration began), <c>done</c>; careful path <c>files</c>, <c>write</c>, <c>review</c>, <c>done</c>.
    /// </summary>
    public static OrderProgress Map(OrderDisplay display, OrderMode mode, bool hasSources, IReadOnlyList<RunEventRow> events, DateTimeOffset orderCreatedAt, DateTimeOffset? finishedAt, DateTimeOffset now) =>
        throw new NotImplementedException();
}
