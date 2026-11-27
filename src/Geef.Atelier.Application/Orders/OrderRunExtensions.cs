using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Warnings;

namespace Geef.Atelier.Application.Orders;

/// <summary>What every run of an order shares, whether it is the first (at submission or after the intake) or a change of a delivered text.</summary>
internal static class OrderRunExtensions
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The crew snapshot of a new run of an order: the code-only direct template.</summary>
    public static Task<ResolvedCrewSnapshot> ResolveOrderSnapshotAsync(this ICrewService crewService, CancellationToken ct) =>
        crewService.ResolveCodeTemplateSnapshotAsync(SystemCrew.DirectTemplate, ct);

    /// <summary>The pending run version 1 of <paramref name="order"/>.</summary>
    public static RunEntity ToFirstRun(this ResolvedCrewSnapshot resolved, OrderEntity order, string briefing, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = now,
        Status = RunStatus.Pending,
        BriefingText = briefing,
        CreatedByUser = order.CreatedByUser,
        CrewTemplateName = resolved.Snapshot.TemplateName,
        CrewSnapshot = JsonSerializer.Serialize(resolved.Snapshot, SnapshotJsonOptions),
        TokensTotal = 0,
        OrderId = order.Id,
        OrderVersion = 1,
    };

    /// <summary>
    /// The pending run that changes <paramref name="baseRun"/> (the shown version) into <paramref name="version"/>: seeded with
    /// its delivered text, carrying the change of the person in <see cref="RunEntity.RevisionHint"/> and the briefing with the change
    /// block. The version is checked again when the run is stored.
    /// </summary>
    public static RunEntity ToRevisionRun(this ResolvedCrewSnapshot resolved, OrderEntity order, RunEntity baseRun, string briefing, string change, int version, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = now,
        Status = RunStatus.Pending,
        BriefingText = briefing,
        CreatedByUser = order.CreatedByUser,
        CrewTemplateName = resolved.Snapshot.TemplateName,
        CrewSnapshot = JsonSerializer.Serialize(resolved.Snapshot, SnapshotJsonOptions),
        TokensTotal = 0,
        OrderId = order.Id,
        OrderVersion = version,
        ParentRunId = baseRun.Id,
        SeedDraftText = baseRun.FinalText,
        RevisionHint = change,
    };

    /// <summary>Stores one plain-English warning per model that could not be assigned as planned.</summary>
    public static async Task ReportAssignmentWarningsAsync(this IRunWarningSink sink, Guid runId, IReadOnlyList<ModelAssignmentWarning> warnings)
    {
        foreach (var warning in warnings)
            await sink.ReportAsync(runId, RunWarningSources.ModelAssignment, RunWarningCodes.ModelAssignment,
                $"The AI model for \"{warning.ShownName}\" could not be chosen as planned, so a different one was used.");
    }
}
