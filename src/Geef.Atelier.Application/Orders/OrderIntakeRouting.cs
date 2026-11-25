using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Application.Orders;

/// <summary>How a new order is started.</summary>
internal enum OrderIntakeRoute
{
    /// <summary>Run v1 at once, as in stage 1.</summary>
    StartNow = 0,

    /// <summary>Run v1 at once, plus a shadow intake that changes nothing.</summary>
    StartNowWithShadow = 1,

    /// <summary>No run yet; the order waits in <c>Analyzing</c> for the intake.</summary>
    Clarify = 2,
}

/// <summary>The switch of <c>SubmitAsync</c> between clarifying first and starting at once.</summary>
internal static class OrderIntakeRouting
{
    /// <summary>
    /// <see cref="OrderIntakeRoute.Clarify"/> when the mode is On and nothing bypasses the intake (questions not allowed, custom
    /// crew, request longer than <paramref name="skipAboveChars"/>); in Shadow mode the same orders get a shadow intake.
    /// </summary>
    public static OrderIntakeRoute Decide(IntakeMode mode, bool allowQuestions, bool customCrew, int requestChars, int skipAboveChars)
    {
        var eligible = allowQuestions && !customCrew && requestChars <= skipAboveChars;
        return mode switch
        {
            IntakeMode.On when eligible => OrderIntakeRoute.Clarify,
            IntakeMode.Shadow when eligible => OrderIntakeRoute.StartNowWithShadow,
            _ => OrderIntakeRoute.StartNow,
        };
    }
}
