namespace Geef.Atelier.Web.Components;

/// <summary>
/// Serializes the data access of all components in one Blazor circuit. The scoped services of a circuit share one
/// <c>DbContext</c>, which allows only one operation at a time; a live-update reload and a user action (filter,
/// delete, resume, cancel) can otherwise overlap while one of them awaits the database. Registered per circuit
/// (scoped). Calls must not be nested: a gated action never calls another gated action.
/// </summary>
public sealed class CircuitDataGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Runs <paramref name="action"/> once no other gated data access of this circuit is in progress.</summary>
    public async Task RunAsync(Func<Task> action)
    {
        await _gate.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs <paramref name="action"/> once no other gated data access of this circuit is in progress.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        await _gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            _gate.Release();
        }
    }
}
