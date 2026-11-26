using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Persistence.Configurations;
using Geef.Atelier.Infrastructure.Persistence.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>
/// Counts refused files in a short-lived scope of its own, so a failed insert never touches the caller's database context.
/// Stores only the way, the code, the detected kind, a clean extension, a clean media type and the size; any failure is
/// logged as a warning and the row is left out.
/// </summary>
internal sealed class SourceRefusalLog(IServiceScopeFactory scopeFactory, TimeProvider time, ILogger<SourceRefusalLog> logger) : ISourceRefusalLog
{
    public async Task RecordAsync(SourceRefusalRecord record, CancellationToken ct = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(record);
            var entity = new SourceRefusalEntity
            {
                Id = Guid.NewGuid(),
                CreatedAt = time.GetUtcNow(),
                OwnerUsername = record.OwnerUsername,
                Path = record.Path,
                Code = Clip(record.Code, SourceRefusalConfiguration.CodeLength) is { Length: > 0 } code ? code : SourceRefusalCodes.Other,
                Kind = record.Kind,
                Extension = SourceRefusalCodes.ExtensionOf(record.Extension is null ? null : "." + record.Extension),
                DeclaredType = SourceRefusalCodes.DeclaredTypeOf(record.DeclaredType),
                SizeBytes = record.SizeBytes is >= 0 ? record.SizeBytes : null,
            };

            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SourceRefusalRepository>().AddAsync(entity, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning("A refused file could not be counted ({Error}); the answer is unchanged.", ex.GetType().Name);
        }
    }

    private static string Clip(string? value, int length) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= length ? value : value[..length];
}
