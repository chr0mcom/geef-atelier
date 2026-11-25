using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Core.Persistence;

/// <summary>
/// Writes an account together with what hangs on its user name: the "About me" profile and the copies of the profile block in the
/// briefings of its runs. Every call runs in one database transaction, so an account never ends up without its profile change.
/// </summary>
public interface IAccountStore
{
    /// <summary>Adds <paramref name="user"/>; a profile left under its name is deleted first.</summary>
    Task AddAsync(AtelierUser user, CancellationToken ct);

    /// <summary>
    /// Saves <paramref name="updated"/>. When the user name changes from <paramref name="previousUsername"/>, the profile moves to
    /// the new name (a profile left under the new name is deleted first) and the runs that stay under the old name lose their copy
    /// of the profile block, so that a later account with the old name never sees it.
    /// </summary>
    Task UpdateAsync(AtelierUser updated, string previousUsername, CancellationToken ct);

    /// <summary>Deletes <paramref name="user"/> and its profile; the runs of the account lose their copy of the profile block.</summary>
    Task DeleteAsync(AtelierUser user, CancellationToken ct);
}
