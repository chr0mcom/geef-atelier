namespace Geef.Atelier.Application.Security;

/// <summary>
/// Central gate for configuration fields that name an environment variable. Only names on the allow list
/// (<see cref="Geef.Atelier.Core.Configuration.EnvReferenceOptions"/>) may be saved or read.
/// </summary>
public interface IEnvReferencePolicy
{
    /// <summary>True when <paramref name="name"/> is a syntactically valid name on the allow list.</summary>
    bool IsAllowed(string? name);

    /// <summary>
    /// Throws <see cref="EnvReferenceNotAllowedException"/> when <paramref name="name"/> is set and not allowed.
    /// Empty or null names pass. <paramref name="field"/> names the field in the error message.
    /// </summary>
    void EnsureAllowed(string? name, string field);

    /// <summary>
    /// Returns the value of the environment variable when <paramref name="name"/> is allowed, otherwise null without
    /// reading it (a warning naming the variable and <paramref name="usage"/> is logged). Null or empty names yield null.
    /// </summary>
    string? Read(string? name, string usage);
}
