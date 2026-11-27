using Geef.Atelier.Core.Domain.Warnings;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>Collects reported warnings in memory.</summary>
internal sealed class RecordingWarningSink : IRunWarningSink
{
    private readonly List<RunWarningEntity> _warnings = [];

    public IReadOnlyList<RunWarningEntity> Warnings
    {
        get { lock (_warnings) return _warnings.ToList(); }
    }

    public Task ReportAsync(Guid runId, string source, string code, string message, int? iteration = null, CancellationToken ct = default)
    {
        lock (_warnings)
        {
            _warnings.Add(new RunWarningEntity
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                Iteration = iteration,
                Source = source,
                Code = code,
                Message = message,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        return Task.CompletedTask;
    }
}
