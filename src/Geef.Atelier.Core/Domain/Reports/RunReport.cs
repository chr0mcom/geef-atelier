using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Core.Domain.Reports;

/// <summary>A source as listed in a run report.</summary>
public sealed record RunReportSource(Guid? SourceId, string FileName, SourceUsageMode Mode, int Characters);

/// <summary>Structured report of a run, stored as JSON in <see cref="RunEntity.ReportJson"/>. Later schema versions only add fields.</summary>
public sealed record RunReport(
    int SchemaVersion,
    string Kind,
    OrderConstraints Constraints,
    IReadOnlyList<DraftCheckResult> Checks,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<RunReportSource> Sources,
    int? DeliveredIteration,
    int Iterations)
{
    public const int CurrentSchemaVersion = 1;
    public const string DirectKind = "direct";
}

/// <summary>JSON form (camelCase) of <see cref="RunReport"/>.</summary>
public static class RunReportJson
{
    /// <summary>Serializes <paramref name="report"/> to camelCase JSON.</summary>
    public static string Serialize(RunReport report) =>
        throw new NotImplementedException();

    /// <summary>Parses a stored report; <c>null</c> for null, blank or unreadable JSON.</summary>
    public static RunReport? TryParse(string? json) =>
        throw new NotImplementedException();
}
