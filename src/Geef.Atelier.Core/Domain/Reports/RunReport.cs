using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Core.Domain.Reports;

/// <summary>A source as listed in a run report; <see cref="LookedUp"/> marks the passages looked up on the web (<c>W1</c>).</summary>
public sealed record RunReportSource(Guid? SourceId, string FileName, SourceUsageMode Mode, int Characters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool LookedUp = false);

/// <summary>A passage the writer quoted from a file, as the code found it in that file.</summary>
public sealed record RunReportQuote(Guid? SourceId, string SourceLabel, string FileName, string Quote, QuoteMatchKind Match);

/// <summary>
/// What an entry of <see cref="RunReport.EarlierOpenPoints"/> is: an open point or an assumption of the writer, or a fixed sentence by
/// Atelier about what of that version could not be shown or taken over (<see cref="Note"/>), which counts as an open point.
/// </summary>
public enum EarlierPointKind { OpenPoint = 0, Assumption = 1, Note = 2 }

/// <summary>
/// An open point or an assumption (<see cref="Kind"/>) of the writer for the earlier <see cref="Version"/> of the order that this version
/// does not repeat, or a fixed sentence about what of that version could not be shown or taken over.
/// </summary>
public sealed record RunReportEarlierPoint(int Version, string Text,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] EarlierPointKind Kind = EarlierPointKind.OpenPoint);

/// <summary>A problem found in round 1 and gone in the delivered version: a check code or a fact finding with its round-1 quote.</summary>
public sealed record RunReportCorrection(string Code, string? QuoteBefore);

/// <summary>
/// Structured report of a run, stored as JSON in <see cref="RunEntity.ReportJson"/>. Later schema versions only add
/// fields. <see cref="ReadEvidence"/> is the input-token comparison of the first writing pass (null when it could
/// not be judged); <see cref="Notes"/> are plain-English remarks for the person, such as files that were searched
/// rather than read in full. Schema 2 adds the writer's notes, the verified quotes, the fact check, the recalculated totals
/// of the files and the corrections after the checks. <see cref="LookedUp"/> says that the passages looked up on the web (<c>W1</c>) were
/// part of the material of the run. <see cref="EarlierOpenPoints"/> are the open points and assumptions of the writer for the version this
/// run changed that it does not repeat (never dropped). <see cref="Revision"/> says that the run changed an earlier version, so that the
/// rules read from the first request were only notes. <see cref="EarlierNeedsCheck"/> says that the version this run changed was shown as
/// "Ready – please check" and its checks could not be taken over (it had no readable report), or that its report says so of the version
/// before it: nothing of it was checked again, so this version is shown the same way (<see cref="Orders.OrderStateResolver"/>).
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
    IReadOnlyList<RunReportCorrection>? Corrections = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool LookedUp = false,
    IReadOnlyList<RunReportEarlierPoint>? EarlierOpenPoints = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Revision = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool EarlierNeedsCheck = false)
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
