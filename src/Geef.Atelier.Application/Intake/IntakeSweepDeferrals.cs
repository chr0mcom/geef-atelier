using System.Collections.Concurrent;

namespace Geef.Atelier.Application.Intake;

/// <summary>
/// Orders whose automatic start failed in a sweep. Each failure leaves the order out for longer: <see cref="FirstDeferral"/>, then
/// doubled with every further failure up to <see cref="MaxDeferral"/>. Orders that keep failing are therefore tried less and less
/// often and can never starve the younger ones, however many there are. Kept in memory: after a restart every order is tried again;
/// an order that has not failed again for <see cref="MaxDeferral"/> after its pause starts over.
/// </summary>
internal sealed class IntakeSweepDeferrals(TimeProvider time)
{
    /// <summary>Pause after the first failure.</summary>
    public static readonly TimeSpan FirstDeferral = TimeSpan.FromMinutes(15);

    /// <summary>Longest pause.</summary>
    public static readonly TimeSpan MaxDeferral = TimeSpan.FromHours(24);

    /// <summary>Most orders kept; the ones that are due soonest are dropped first.</summary>
    public const int MaxEntries = 10_000;

    private sealed record Entry(int Failures, DateTimeOffset Until);

    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();

    /// <summary>The orders that are left out now.</summary>
    public IReadOnlySet<Guid> Active()
    {
        var now = time.GetUtcNow();
        var active = new HashSet<Guid>();
        foreach (var (id, entry) in _entries)
        {
            if (entry.Until > now)
                active.Add(id);
            else if (entry.Until + MaxDeferral <= now)
                _entries.TryRemove(id, out _);
        }
        return active;
    }

    /// <summary>The pause after <paramref name="failures"/> failures in a row.</summary>
    public static TimeSpan PauseAfter(int failures)
    {
        var pause = FirstDeferral;
        for (var i = 1; i < failures && pause < MaxDeferral; i++)
            pause += pause;
        return pause < MaxDeferral ? pause : MaxDeferral;
    }

    /// <summary>Leaves <paramref name="orderId"/> out of the sweeps for the pause its failures earn; returns that pause.</summary>
    public TimeSpan Defer(Guid orderId)
    {
        if (_entries.Count >= MaxEntries && !_entries.ContainsKey(orderId))
        {
            foreach (var (id, _) in _entries.OrderBy(e => e.Value.Until).Take(_entries.Count - MaxEntries + 1))
                _entries.TryRemove(id, out _);
        }
        var now = time.GetUtcNow();
        var entry = _entries.AddOrUpdate(
            orderId,
            _ => new Entry(1, now + PauseAfter(1)),
            (_, previous) => new Entry(previous.Failures + 1, now + PauseAfter(previous.Failures + 1)));
        return entry.Until - now;
    }
}
