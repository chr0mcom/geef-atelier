using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// <see cref="IAccountStore"/> over the in-memory user and profile repositories, all or nothing like the real transaction. With
/// <see cref="FailNext"/> the next call throws before it changes anything (a failure inside the transaction rolls everything back).
/// </summary>
public sealed class InMemoryAccountStore(InMemoryAtelierUserRepository users, InMemoryUserProfileRepository profiles) : IAccountStore
{
    public bool FailNext { get; set; }

    public async Task AddAsync(AtelierUser user, CancellationToken ct)
    {
        ThrowIfFailing();
        await profiles.DeleteAsync(user.Username, ct);
        await users.AddAsync(user, ct);
    }

    public async Task UpdateAsync(AtelierUser updated, string previousUsername, CancellationToken ct)
    {
        ThrowIfFailing();
        await users.UpdateAsync(updated, ct);
        if (string.Equals(previousUsername, updated.Username, StringComparison.Ordinal))
            return;
        var profile = await profiles.GetAsync(previousUsername, ct);
        await profiles.DeleteAsync(updated.Username, ct);
        if (profile is null)
            return;
        await profiles.UpsertAsync(profile with { Username = updated.Username }, ct);
        await profiles.DeleteAsync(previousUsername, ct);
    }

    public async Task DeleteAsync(AtelierUser user, CancellationToken ct)
    {
        ThrowIfFailing();
        await users.DeleteAsync(user.UserId, ct);
        await profiles.DeleteAsync(user.Username, ct);
    }

    private void ThrowIfFailing()
    {
        if (!FailNext)
            return;
        FailNext = false;
        throw new InvalidOperationException("Simulated database failure.");
    }
}
