using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Application.Orders;

/// <summary>Builds the run briefing from an order. Stage 1: the request verbatim. Stage 2 (<c>Intake:BriefMode</c>): request plus answers, or a prepared brief.</summary>
public interface IOrderBriefComposer
{
    /// <summary>Returns the briefing for the next run of <paramref name="order"/>.</summary>
    string Compose(OrderEntity order);
}

internal sealed class VerbatimOrderBriefComposer : IOrderBriefComposer
{
    public string Compose(OrderEntity order) =>
        throw new NotImplementedException();
}
