using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Research;

namespace Geef.Atelier.Application.Research;

/// <summary>The research of an order: looked up once before its first run and then used unchanged by every run.</summary>
public interface IOrderResearchService
{
    /// <summary>
    /// Returns the stored research of <paramref name="order"/>, or does new research when the first run of the order allows it
    /// (<paramref name="orderVersion"/> 1, no text to revise, mode on, clarified with research topics) and stores it; <c>null</c> when
    /// there is none and none is needed. An order with attached files (<paramref name="hasFiles"/>, any role) never searches and gets
    /// <see cref="ResearchStatus.FilesAttached"/> when it has research topics. <paramref name="profileText"/> is the About me block of the
    /// briefing. Never throws for failures of the research itself: they end as a status.
    /// </summary>
    Task<OrderResearch?> EnsureAsync(OrderEntity order, int? orderVersion, bool revises, bool hasFiles, string? profileText, CancellationToken ct);
}
