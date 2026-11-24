using System.Text.RegularExpressions;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Security;

/// <summary>Allow-list implementation of <see cref="IEnvReferencePolicy"/>; matching is case-sensitive.</summary>
public sealed partial class EnvReferencePolicy(
    IOptionsMonitor<EnvReferenceOptions> options,
    ILogger<EnvReferencePolicy> logger) : IEnvReferencePolicy
{
    /// <summary>Policy with only <see cref="EnvReferenceOptions.DefaultAllowed"/>, for callers built without DI.</summary>
    public static IEnvReferencePolicy Default { get; } =
        new EnvReferencePolicy(new FixedOptionsMonitor(new EnvReferenceOptions()), NullLogger<EnvReferencePolicy>.Instance);

    /// <inheritdoc/>
    public bool IsAllowed(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !NamePattern().IsMatch(name))
            return false;
        return EnvReferenceOptions.DefaultAllowed.Concat(options.CurrentValue.Allowed ?? [])
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Select(entry => entry.Trim())
            .Any(entry => entry.EndsWith('*')
                ? entry.Length > 1 && name.Length > entry.Length - 1 && name.StartsWith(entry[..^1], StringComparison.Ordinal)
                : string.Equals(entry, name, StringComparison.Ordinal));
    }

    /// <inheritdoc/>
    public void EnsureAllowed(string? name, string field)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (!IsAllowed(name))
            throw new EnvReferenceNotAllowedException(field, name.Trim());
    }

    /// <inheritdoc/>
    public string? Read(string? name, string usage)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (IsAllowed(name))
            return Environment.GetEnvironmentVariable(name);
        logger.LogWarning(
            "Environment-variable reference '{Name}' for {Usage} is not on the allow list (EnvReference:Allowed); ignored, value not read.",
            name, usage);
        return null;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex NamePattern();

    private sealed class FixedOptionsMonitor(EnvReferenceOptions value) : IOptionsMonitor<EnvReferenceOptions>
    {
        public EnvReferenceOptions CurrentValue => value;

        public EnvReferenceOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<EnvReferenceOptions, string?> listener) => null;
    }
}
