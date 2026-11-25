using System.Collections.Concurrent;

namespace Geef.Atelier.Application.Intake;

/// <summary>
/// Orders whose automatic start failed in a sweep. They are left out for <see cref="DeferFor"/>, so that one order that keeps
/// failing can neither block the younger ones nor fill the batch. Kept in memory: after a restart every order is tried again.
/// </summary>
internal sealed class IntakeSweepDeferrals(TimeProvider time)
{
    /// <summary>How long a failed order is left out of the sweeps.</summary>
    public static readonly TimeSpan DeferFor = TimeSpan.FromMinutes(15);

    /// <summary>Most orders kept; the ones that are due soonest are dropped first.</summary>
    public const int MaxEntries = 500;

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _until = new();

    /// <summary>The orders that are left out now.</summary>
    public IReadOnlySet<Guid> Active()
    {
        var now = time.GetUtcNow();
        foreach (var (id, until) in _until)
        {
            if (until <= now)
                _until.TryRemove(id, out _);
        }
        return _until.Keys.ToHashSet();
    }

    /// <summary>Leaves <paramref name="orderId"/> out of the sweeps for <see cref="DeferFor"/>.</summary>
    public void Defer(Guid orderId)
    {
        if (_until.Count >= MaxEntries)
        {
            foreach (var (id, _) in _until.OrderBy(e => e.Value).Take(_until.Count - MaxEntries + 1))
                _until.TryRemove(id, out _);
        }
        _until[orderId] = time.GetUtcNow() + DeferFor;
    }
}
