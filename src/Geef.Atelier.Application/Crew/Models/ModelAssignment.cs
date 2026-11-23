using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Application.Crew.Models;

/// <summary>Result of assigning a model to one actor.</summary>
/// <param name="Candidate">Assigned provider/model.</param>
/// <param name="IsFallback">True when no candidate survived the rules and the slot's first candidate was used.</param>
/// <param name="Warning">Human-readable reason when the primary candidate was skipped or a fallback was used.</param>
public sealed record ModelAssignment(ModelCandidate Candidate, bool IsFallback, string? Warning);
