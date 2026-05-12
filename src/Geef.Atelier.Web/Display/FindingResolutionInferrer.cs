using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Web.Display;

/// <summary>Heuristic cross-iteration diff: marks findings as resolved when absent in the next iteration.</summary>
public static class FindingResolutionInferrer
{
    public static IReadOnlyDictionary<Guid, bool> MarkResolved(IReadOnlyList<IterationWithFindings> iterations)
    {
        var result = new Dictionary<Guid, bool>();

        if (iterations.Count == 0)
            return result;

        for (var i = 0; i < iterations.Count - 1; i++)
        {
            var current = iterations[i].Findings;
            var next    = iterations[i + 1].Findings;

            var nextSignatures = next
                .Select(f => Signature(f))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var finding in current)
                result[finding.Id] = !nextSignatures.Contains(Signature(finding));
        }

        // Last iteration's findings are never marked resolved
        foreach (var finding in iterations[^1].Findings)
            result.TryAdd(finding.Id, false);

        return result;
    }

    private static string Signature(FindingEntity f) =>
        $"{f.Severity}|{f.ReviewerName}|{f.Message[..Math.Min(60, f.Message.Length)]}";
}
