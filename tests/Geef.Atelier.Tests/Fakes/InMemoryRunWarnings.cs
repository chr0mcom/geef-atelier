using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>Warning sink and repository in one: what is reported can be listed again, per run.</summary>
internal sealed class InMemoryRunWarnings : IRunWarningSink, IRunWarningRepository
{
    private readonly List<RunWarningEntity> _warnings = [];

    public IReadOnlyList<RunWarningEntity> All
    {
        get
        {
            lock (_warnings)
                return _warnings.ToList();
        }
    }

    public Task ReportAsync(Guid runId, string source, string code, string message, int? iteration = null, CancellationToken ct = default)
    {
        lock (_warnings)
            _warnings.Add(new RunWarningEntity
            {
                Id = Guid.NewGuid(), RunId = runId, Iteration = iteration, Source = source, Code = code, Message = message,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RunWarningEntity>> ListByRunAsync(Guid runId, CancellationToken ct = default)
    {
        lock (_warnings)
            return Task.FromResult<IReadOnlyList<RunWarningEntity>>(_warnings.Where(w => w.RunId == runId).ToList());
    }
}
