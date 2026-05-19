namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Live status of the active press (runs currently in Pending or Running state).</summary>
public sealed record PressStatus(
    PressState State,
    IReadOnlyList<PressRun> Runs,
    DateTimeOffset? IdleSince);

/// <summary>Phase rail position for a single active run.</summary>
public sealed record PressRun(
    Guid RunId,
    string? TemplateName,
    int Phase,    // 0=Grounding, 1=Execution, 2=Evaluation, 3=Finalize
    int Iteration,
    int MaxIterations,
    DateTimeOffset StartedAt);

public enum PressState
{
    Idle   = 0,
    Single = 1,
    Multi  = 2
}
