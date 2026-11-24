using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Application.Runs;

public sealed record SubmitRunRequest(
    string BriefingText,
    string ConfigJson,
    string? CreatedByUser = null,
    string? CrewTemplateName = null,
    CrewSpec? CustomCrew = null,
    IReadOnlyList<RunAttachmentInput>? Attachments = null,
    RunKind Kind = RunKind.Standard,
    bool AutoCompose = false,
    bool ChainToTaskRun = true,
    Guid? ParentCompositionRunId = null,
    OrderEntry Entry = OrderEntry.Web);
