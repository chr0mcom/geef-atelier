using Geef.Atelier.Core.Domain.Crew.TemplateStudio;

namespace Geef.Atelier.Application.Crew.TemplateStudio;

/// <summary>User-confirmed (and possibly edited) proposal to persist as crew records.</summary>
public sealed record MaterializationRequest(
    ProposedTemplate FinalTemplate,
    IReadOnlyList<ProposedProfile> FinalNewProfiles);
