using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Application.Crew;

/// <summary>
/// Builds fully-dereferenced <see cref="CrewSnapshot"/> instances from templates or inline specs.
/// Profile lookups are provided via callbacks so the builder remains testable without EF dependencies.
/// </summary>
public static class CrewSnapshotBuilder
{
    /// <summary>Builds a snapshot from a named crew template, resolving all referenced profiles.</summary>
    public static async Task<CrewSnapshot> BuildAsync(
        CrewTemplate template,
        Func<string, CancellationToken, Task<ExecutorProfile?>> executorLookup,
        Func<string, CancellationToken, Task<ReviewerProfile?>> reviewerLookup,
        CancellationToken cancellationToken = default)
    {
        var executor = await executorLookup(template.ExecutorProfileName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Executor profile '{template.ExecutorProfileName}' referenced by template '{template.Name}' not found.");

        var reviewers = await ResolveReviewersAsync(template.ReviewerProfileNames, reviewerLookup, cancellationToken);

        return new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: template.Name,
            Executor: executor,
            Reviewers: reviewers,
            EvaluationStrategy: template.EvaluationStrategy,
            ConvergenceOverride: template.ConvergenceOverride,
            Advisors: Array.Empty<AdvisorProfile>());
    }

    /// <summary>Builds a snapshot from an inline crew spec (no template name), resolving all referenced profiles.</summary>
    public static async Task<CrewSnapshot> BuildAsync(
        CrewSpec spec,
        Func<string, CancellationToken, Task<ExecutorProfile?>> executorLookup,
        Func<string, CancellationToken, Task<ReviewerProfile?>> reviewerLookup,
        CancellationToken cancellationToken = default)
    {
        var executor = await executorLookup(spec.ExecutorProfileName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Executor profile '{spec.ExecutorProfileName}' referenced by inline crew spec not found.");

        var reviewers = await ResolveReviewersAsync(spec.ReviewerProfileNames, reviewerLookup, cancellationToken);

        return new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: null,
            Executor: executor,
            Reviewers: reviewers,
            EvaluationStrategy: spec.EvaluationStrategy,
            ConvergenceOverride: spec.ConvergenceOverride,
            Advisors: Array.Empty<AdvisorProfile>());
    }

    private static async Task<IReadOnlyList<ReviewerProfile>> ResolveReviewersAsync(
        IReadOnlyList<string> names,
        Func<string, CancellationToken, Task<ReviewerProfile?>> reviewerLookup,
        CancellationToken cancellationToken)
    {
        var reviewers = new List<ReviewerProfile>(names.Count);
        foreach (var name in names)
        {
            var reviewer = await reviewerLookup(name, cancellationToken)
                ?? throw new InvalidOperationException($"Reviewer profile '{name}' not found.");
            reviewers.Add(reviewer);
        }
        return reviewers;
    }
}
