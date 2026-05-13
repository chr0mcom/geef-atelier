namespace Geef.Atelier.Core.Domain.Crew.Advisors;

/// <summary>
/// Immutable record of a single advisor consultation within a pipeline run.
/// </summary>
/// <param name="Id">Unique identifier for this consultation record.</param>
/// <param name="RunId">The run this consultation belongs to.</param>
/// <param name="IterationNumber">
/// The executor iteration this consultation preceded. A value of <c>-1</c> is the convention for
/// convergence-failure recovery consultations; normal iterations begin at <c>1</c>.
/// </param>
/// <param name="AdvisorProfileName">Name of the <see cref="AdvisorProfile"/> that produced this output.</param>
/// <param name="Output">The raw text output returned by the advisor LLM.</param>
/// <param name="CreatedAt">UTC timestamp when the consultation was recorded.</param>
public sealed record AdvisorConsultation(
    Guid Id,
    Guid RunId,
    int IterationNumber,
    string AdvisorProfileName,
    string Output,
    DateTimeOffset CreatedAt);
