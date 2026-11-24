using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>Turns intake answers into checks without a text parser.</summary>
public static class IntakeConstraints
{
    /// <summary>Adds the answered length to <paramref name="fromRequest"/> when the request itself has none; everything else unchanged.</summary>
    public static OrderConstraints WithAnswers(OrderConstraints fromRequest, IntakeRecord? record, IReadOnlyList<OrderAnswer> answers) =>
        throw new NotImplementedException();
}
