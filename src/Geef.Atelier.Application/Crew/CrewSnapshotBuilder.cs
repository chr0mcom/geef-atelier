using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Application.Crew;

/// <summary>Builds fully-dereferenced <see cref="CrewSnapshot"/> instances from code-only templates.</summary>
public static class CrewSnapshotBuilder
{
    /// <summary>
    /// The snapshot of <paramref name="template"/> with the writer and check profiles it names, looked up in
    /// <paramref name="executors"/> and <paramref name="reviewers"/>. Throws <see cref="InvalidOperationException"/> when a profile is missing.
    /// </summary>
    public static CrewSnapshot Build(
        CrewTemplate template,
        IReadOnlyDictionary<string, ExecutorProfile> executors,
        IReadOnlyDictionary<string, ReviewerProfile> reviewers)
    {
        var executor = executors.TryGetValue(template.ExecutorProfileName, out var e)
            ? e
            : throw new InvalidOperationException(
                $"Executor profile '{template.ExecutorProfileName}' referenced by template '{template.Name}' not found.");
        var checks = template.ReviewerProfileNames
            .Select(name => reviewers.TryGetValue(name, out var r)
                ? r
                : throw new InvalidOperationException($"Reviewer profile '{name}' referenced by template '{template.Name}' not found."))
            .ToList();

        return new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: template.Name,
            Executor: executor,
            Reviewers: checks,
            EvaluationStrategy: template.EvaluationStrategy,
            ConvergenceOverride: template.ConvergenceOverride,
            CrewDomain: template.Domain ?? Core.Domain.Crew.Models.ModelDomain.General);
    }
}
