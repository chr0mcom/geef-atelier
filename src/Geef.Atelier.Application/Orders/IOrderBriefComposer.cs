using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;

namespace Geef.Atelier.Application.Orders;

/// <summary>What the briefing of the next run is built from.</summary>
public sealed record OrderBriefInput(OrderEntity Order, IntakeRecord? Record, IReadOnlyList<OrderAnswer> Answers, UserProfileEntity? Profile);

/// <summary>The briefing text and the names of the sections it contains, in order.</summary>
public sealed record IntakeBrief(string Text, IReadOnlyList<string> Sections);

/// <summary>Builds the run briefing from an order. Stage 1: the request verbatim. Stage 2 (<c>Intake:BriefMode</c>): request plus answers, or a prepared brief.</summary>
public interface IOrderBriefComposer
{
    /// <summary>The briefing of the next run. Without record, answers and profile exactly the request (stage 1, bit-identical).</summary>
    IntakeBrief Compose(OrderBriefInput input);
}

internal sealed class IntakeBriefComposer : IOrderBriefComposer
{
    public IntakeBrief Compose(OrderBriefInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Order);
        return new IntakeBrief(input.Order.RequestText, ["request"]);
    }
}
