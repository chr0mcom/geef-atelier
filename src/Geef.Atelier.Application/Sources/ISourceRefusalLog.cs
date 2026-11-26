using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Application.Sources;

/// <summary>Counts refused files. Only observes: never throws and never changes an answer; a failure leaves the row out and logs a warning.</summary>
public interface ISourceRefusalLog
{
    /// <summary>Stores <paramref name="record"/> in its own short-lived database scope.</summary>
    Task RecordAsync(SourceRefusalRecord record, CancellationToken ct = default);
}
