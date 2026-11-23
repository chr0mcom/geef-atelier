using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Application.Crew.Models;

/// <summary>One reviewer to assign as part of a crew.</summary>
/// <param name="Slot">Model-matrix slot; <c>null</c> for a manual reviewer that keeps <paramref name="Current"/>.</param>
/// <param name="ProfileDomain">Profile-level domain override; <c>null</c> inherits the crew domain.</param>
/// <param name="Current">Binding currently stored on the profile.</param>
public sealed record ReviewerAssignmentRequest(ModelSlot? Slot, ModelDomain? ProfileDomain, ModelCandidate Current);
