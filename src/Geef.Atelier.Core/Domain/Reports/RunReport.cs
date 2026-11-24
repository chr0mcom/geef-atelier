using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Core.Domain.Reports;

/// <summary>A source as listed in a run report.</summary>
public sealed record RunReportSource(Guid? SourceId, string FileName, SourceUsageMode Mode, int Characters);

/// <summary>
/// Structured report of a run, stored as JSON in <see cref="RunEntity.ReportJson"/>. Later schema versions only add
/// fields. <see cref="ReadEvidence"/> is the input-token comparison of the first writing pass (null when it could
/// not be judged); <see cref="Notes"/> are plain-English remarks for the person, such as files that were searched
/// rather than read in full.
/// </summary>
public sealed record RunReport(
    int SchemaVersion,
    string Kind,
    OrderConstraints Constraints,
    IReadOnlyList<DraftCheckResult> Checks,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<RunReportSource> Sources,
    int? DeliveredIteration,
    int Iterations,
    ReadEvidence? ReadEvidence = null,
    IReadOnlyList<string>? Notes = null)
{
    public const int CurrentSchemaVersion = 1;
    public const string DirectKind = "direct";

    /// <summary>Checks of the delivered text that did not pass.</summary>
    [JsonIgnore]
    public IReadOnlyList<DraftCheckResult> OpenChecks => Checks.Where(c => !c.Passed).ToList();
}

/// <summary>JSON form (camelCase, enums as text) of <see cref="RunReport"/>.</summary>
public static class RunReportJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes <paramref name="report"/> to camelCase JSON.</summary>
    public static string Serialize(RunReport report) =>
        JsonSerializer.Serialize(report, JsonOptions);

    /// <summary>Parses a stored report; <c>null</c> for null, blank or unreadable JSON. Missing lists become empty.</summary>
    public static RunReport? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        RunReport? report;
        try
        {
            report = JsonSerializer.Deserialize<RunReport>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }

        if (report is null)
            return null;

        var constraints = report.Constraints ?? OrderConstraints.None;
        return report with
        {
            Kind = report.Kind ?? string.Empty,
            Constraints = constraints with
            {
                ForbiddenPatterns = constraints.ForbiddenPatterns ?? [],
                ForbiddenTerms = constraints.ForbiddenTerms ?? [],
                RequiredTerms = constraints.RequiredTerms ?? [],
                Notes = constraints.Notes ?? [],
            },
            Checks = report.Checks ?? [],
            Placeholders = report.Placeholders ?? [],
            Sources = report.Sources ?? [],
            Notes = report.Notes ?? [],
        };
    }
}
