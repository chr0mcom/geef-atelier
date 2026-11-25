namespace Geef.Atelier.Application.Intake;

/// <summary>What <see cref="IOrderIntakeService"/> did with an order.</summary>
public enum IntakeProcessResult { NotFound = 0, NotAnalyzing = 1, Started = 2, AskedQuestions = 3, StartedAfterFailure = 4, ShadowRecorded = 5, ShadowSkipped = 6 }

/// <summary>Clarifies orders before their first run; called by the intake worker.</summary>
public interface IOrderIntakeService
{
    /// <summary>Runs the intake for a claimed order: load, clarify (with <c>Intake:Timeout</c>), resolve from memory, gate, revision role, then start the run (no questions or failure) or store the questions.</summary>
    Task<IntakeProcessResult> ProcessAsync(Guid orderId, CancellationToken ct);

    /// <summary>Same analysis for an order that already runs; stores only <c>OrderIntakeShadows</c>, changes nothing else.</summary>
    Task<IntakeProcessResult> ProcessShadowAsync(Guid orderId, CancellationToken ct);

    /// <summary>Starts orders that are stuck in the intake (exhausted attempts, queue timeout) without questions.</summary>
    Task<int> StartStuckAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Starts orders whose questions stayed unanswered longer than <c>Intake:NeedsInputAutoStartAfter</c> with the assumptions.</summary>
    Task<int> AutoStartUnansweredAsync(DateTimeOffset now, CancellationToken ct);
}
