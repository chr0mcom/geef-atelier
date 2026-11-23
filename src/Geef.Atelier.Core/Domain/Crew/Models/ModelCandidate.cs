namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>A provider/model pair the model matrix may assign.</summary>
/// <param name="Provider">Geef provider name, e.g. <c>claude-cli</c>.</param>
/// <param name="Model">Model identifier as the provider expects it.</param>
public sealed record ModelCandidate(string Provider, string Model)
{
    /// <summary>True when the candidate is billed per token, i.e. not served by a subscription CLI.</summary>
    public bool IsPaid => Provider is not (ModelMatrix.ClaudeCli or ModelMatrix.CodexCli);

    /// <inheritdoc />
    public override string ToString() => $"{Provider}/{Model}";
}
