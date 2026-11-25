namespace Geef.Atelier.Application.Intake;

/// <summary>Wakes the intake worker right after an order was created, so the first poll does not wait.</summary>
public interface IOrderIntakeSignal
{
    /// <summary>Signals that new work may be waiting.</summary>
    void Wake();

    /// <summary>Returns after a <see cref="Wake"/> or at the latest after <paramref name="max"/>.</summary>
    ValueTask WaitAsync(TimeSpan max, CancellationToken ct);
}
