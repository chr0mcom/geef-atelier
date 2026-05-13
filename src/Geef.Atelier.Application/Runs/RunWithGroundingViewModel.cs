using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Application.Runs;

/// <summary>
/// Aggregated view of a run with advisor consultations grouped by trigger type, ready for UI rendering.
/// </summary>
/// <param name="Details">The run entity together with its iterations and findings.</param>
/// <param name="Snapshot">The deserialized crew snapshot, or null when no snapshot is stored.</param>
/// <param name="GroundedBrief">The briefing text used for this run.</param>
/// <param name="GroundingAdvisors">
/// Consultations produced by advisors with <see cref="AdvisorTrigger.BeforeFirstExecution"/>.
/// </param>
/// <param name="RecoveryAdvisors">
/// Consultations with <see cref="AdvisorConsultation.IterationNumber"/> == -1 (convergence-failure recovery).
/// </param>
/// <param name="AdvisorsByIteration">
/// All remaining consultations (trigger <see cref="AdvisorTrigger.BeforeEveryExecution"/> plus any whose
/// advisor profile is not found in the snapshot) keyed by <see cref="AdvisorConsultation.IterationNumber"/>.
/// </param>
/// <param name="GroundingConsultations">
/// Web-research consultations performed by grounding providers before the pipeline's first iteration.
/// </param>
public sealed record RunWithGroundingViewModel(
    RunDetails Details,
    CrewSnapshot? Snapshot,
    string GroundedBrief,
    IReadOnlyList<AdvisorConsultation> GroundingAdvisors,
    IReadOnlyList<AdvisorConsultation> RecoveryAdvisors,
    ILookup<int, AdvisorConsultation> AdvisorsByIteration,
    IReadOnlyList<GroundingConsultation> GroundingConsultations);
