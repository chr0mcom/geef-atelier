using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Decorator around an <see cref="IExecutionStep"/> that consults pre-execution advisors before
/// delegating to the inner step. Advisor outputs are injected into the run context via the
/// <see cref="AtelierContextKeys.AdvisorBlock"/> key so the inner executor can prepend them.
/// </summary>
internal sealed class AdvisorAwareExecutor : IExecutionStep
{
    private readonly IExecutionStep _inner;
    private readonly IReadOnlyList<ProfileBasedAdvisor> _advisors;
    private readonly Guid _runId;
    private int _iterationCounter;

    public AdvisorAwareExecutor(
        IExecutionStep inner,
        IReadOnlyList<ProfileBasedAdvisor> advisors,
        Guid runId)
    {
        _inner            = inner;
        _advisors         = advisors;
        _runId            = runId;
        _iterationCounter = 0;
    }

    /// <inheritdoc/>
    public async Task<ExecutionResult> RunAsync(IRunContext context, CancellationToken cancellationToken)
    {
        _iterationCounter++;

        var activeAdvisors = _advisors
            .Where(a =>
                a.Profile.Trigger == AdvisorTrigger.BeforeEveryExecution ||
                (a.Profile.Trigger == AdvisorTrigger.BeforeFirstExecution && _iterationCounter == 1))
            .ToList();

        if (activeAdvisors.Count > 0)
        {
            var briefingText = context.GetRequired(AtelierContextKeys.GroundedBrief);
            var consultationOutputs = new List<string>(activeAdvisors.Count);

            foreach (var advisor in activeAdvisors)
            {
                var consultation = await advisor.ConsultAsync(
                    _runId, _iterationCounter, briefingText, cancellationToken);

                consultationOutputs.Add(
                    $"## {advisor.Profile.DisplayName} ({advisor.Profile.Mode})\n{consultation.Output}");
            }

            var advisorBlock =
                $"[Advisor consultations for this iteration]\n\n" +
                $"{string.Join("\n\n", consultationOutputs)}\n\n" +
                $"[End of advisor consultations]";

            context = context.Set(AtelierContextKeys.AdvisorBlock, advisorBlock);
        }

        return await _inner.RunAsync(context, cancellationToken);
    }
}
