namespace Geef.Atelier.Mcp.Models;

/// <summary>Answer of <c>profile</c>: what the person saved under About me in the Atelier web app. Read only; the person changes it there.</summary>
/// <param name="Name">"Your name".</param>
/// <param name="Role">"What you do".</param>
/// <param name="Organization">"Organization".</param>
/// <param name="Formality">How readers are addressed: <c>formal</c> (Sie), <c>informal</c> (du) or <c>null</c> (it depends).</param>
/// <param name="Language">"Language of your texts" as a two-letter code; <c>null</c> when not set.</param>
/// <param name="Signature">"How you sign off".</param>
/// <param name="NeverDo">"Never do this".</param>
/// <param name="Notes">"Anything else I should know".</param>
/// <param name="IsEmpty">True when nothing is saved.</param>
/// <param name="UpdatedAt">When the person last saved it; <c>null</c> when nothing is saved.</param>
/// <param name="Note">Where the person changes these details, in plain English.</param>
public sealed record ProfileDto(
    string? Name,
    string? Role,
    string? Organization,
    string? Formality,
    string? Language,
    string? Signature,
    string? NeverDo,
    string? Notes,
    bool IsEmpty,
    DateTimeOffset? UpdatedAt,
    string Note);
