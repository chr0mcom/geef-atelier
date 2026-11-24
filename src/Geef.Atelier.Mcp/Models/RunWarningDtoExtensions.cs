using Geef.Atelier.Core.Domain.Warnings;

namespace Geef.Atelier.Mcp.Models;

internal static class RunWarningDtoExtensions
{
    public static IReadOnlyList<RunWarningDto> ToDtos(this IReadOnlyList<RunWarningEntity> warnings) =>
        warnings.Select(w => new RunWarningDto(w.Code, w.Message)).ToList();
}
