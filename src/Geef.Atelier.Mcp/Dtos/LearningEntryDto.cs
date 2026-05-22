using Geef.Atelier.Core.Domain.Crew.Learning;

namespace Geef.Atelier.Mcp.Dtos;

public sealed record LearningEntryDto(
    Guid Id,
    string Text,
    Guid SourceRunId,
    Guid? LearningRunId,
    string Domain,
    string Status,
    string OwnerUsername,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ApprovedAt);
