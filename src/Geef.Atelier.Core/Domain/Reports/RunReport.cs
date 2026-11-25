using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Core.Domain.Reports;

/// <summary>A source as listed in a run report.</summary>
public sealed record RunReportSource(Guid? SourceId, string FileName, SourceUsageMode Mode, int Characters);

/// <summary>A passage the writer quoted from a file, as the code found it in that file.</summary>
public sealed record RunReportQuote(Guid? SourceId, string SourceLabel, string FileName, string Quote, QuoteMatchKind Match);

/// <summary>A problem found in round 1 and gone in the delivered version: a check code or a fact finding with its round-1 quote.</summary>
public sealed record RunReportCorrection(string Code, string? QuoteBefore);

/// <summary>
/// Structured report of a run, stored as JSON in <see cref="RunEntity.ReportJson"/>. Later schema versions only add
/// fields. <see cref="ReadEvidence"/> is the input-token comparison of the first writing pass (null when it could
/// not be judged); <see cref="Notes"/> are plain-English remarks for the person, such as files that were searched
/// rather than read in full. Schema 2 adds the writer's notes, the verified quotes, the fact check, the recalculated totals
/// of the files and the corrections after the checks.
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
    IReadOnlyList<string>? Notes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] WriterNotesStatus NotesStatus = WriterNotesStatus.Off,
    WriterNotes? WriterNotes = null,
    IReadOnlyList<RunReportQuote>? SourceQuotes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int UnverifiedQuotes = 0,
    FactCheckSummary? FactCheck = null,
    SourceArithmeticResult? SourceArithmetic = null,
    IReadOnlyList<RunReportCorrection>? Corrections = null)
{
    public const int CurrentSchemaVersion = 2;
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

    /// <summary>Parses a stored report; <c>null</c> for null, blank or unreadable JSON. Missing lists become empty, missing writer notes <see cref="WriterNotes.Empty"/>.</summary>
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
            WriterNotes = report.WriterNotes is { } notes
                ? notes with { Assumptions = notes.Assumptions ?? [], OpenPoints = notes.OpenPoints ?? [], Quotes = notes.Quotes ?? [] }
                : WriterNotes.Empty,
            SourceQuotes = report.SourceQuotes ?? [],
            Corrections = report.Corrections ?? [],
        };
    }
}
