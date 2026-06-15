using System.Text.RegularExpressions;

namespace Geef.Atelier.Core.Domain.Crew.Specialization;

/// <summary>
/// A reusable, scoped specialization layer that is appended to a generic actor role prompt at
/// snapshot-build time. Packs carry the task/domain-specific delta so that actor profiles can stay
/// generic and safely reusable. Several packs can be bound to one actor (ordered).
/// </summary>
/// <param name="Name">
/// Unique kebab-case identifier. System packs use stable short slugs; user-created packs are
/// auto-prefixed with <c>"custom-"</c>. Must satisfy <see cref="IsValidName"/>.
/// </param>
/// <param name="DisplayName">Human-readable label shown in the UI.</param>
/// <param name="Description">Short prose summary of what the pack specializes.</param>
/// <param name="SpecializationText">
/// The task/domain-specific prompt fragment merged into the actor's <c>{specialization}</c> slot.
/// </param>
/// <param name="Scope">Reuse scope; see <see cref="PackScope"/>.</param>
/// <param name="Domain">Required when <see cref="Scope"/> is <see cref="PackScope.DomainScoped"/>; null otherwise.</param>
/// <param name="ApplicableActorTypes">
/// Actor types this pack may be bound to. An empty list or one containing <see cref="PackActorType.Any"/>
/// matches every actor type.
/// </param>
/// <param name="OwningCrewId">
/// Name of the owning crew template when <see cref="Scope"/> is <see cref="PackScope.TaskBound"/>
/// (cascade-deleted with that crew); null otherwise.
/// </param>
/// <param name="IsSystem"><see langword="true"/> for built-in code-constant packs that cannot be modified.</param>
/// <param name="Archived">
/// When <see langword="true"/> the pack is hidden from pickers and the composer catalogue
/// (set by the auto-GC job for long-unused custom packs). System packs are never archived.
/// </param>
/// <param name="CreatedAt">UTC creation timestamp. Null for system packs.</param>
/// <param name="UpdatedAt">UTC timestamp of the last update. Null for system packs.</param>
/// <param name="LastUsedAt">
/// UTC timestamp of the last run that composed this pack into an effective prompt. Used by auto-GC.
/// </param>
public sealed record SpecializationPack(
    string Name,
    string DisplayName,
    string Description,
    string SpecializationText,
    PackScope Scope,
    string? Domain,
    IReadOnlyList<PackActorType> ApplicableActorTypes,
    string? OwningCrewId,
    bool IsSystem,
    bool Archived = false,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null,
    DateTimeOffset? LastUsedAt = null)
{
    private static readonly Regex NameRegex =
        new(@"^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$", RegexOptions.Compiled);

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="name"/> is a valid pack name:
    /// lowercase ASCII letters, digits, and hyphens only, no leading/trailing hyphen, length >= 1.
    /// </summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrEmpty(name) && NameRegex.IsMatch(name);
}
