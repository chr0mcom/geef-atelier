using Geef.Atelier.Core.Domain.Crew;
using Geef.Sdk.Policies;
using DomainStrategy = Geef.Atelier.Core.Domain.Crew.EvaluationStrategy;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class EvaluationStrategyMapper
{
    public static IEvaluationStrategy Map(DomainStrategy strategy) => strategy switch
    {
        DomainStrategy.Parallel   => new ParallelEvaluationStrategy(),
        DomainStrategy.Sequential => new SequentialEvaluationStrategy(),
        DomainStrategy.FailFast   => new FailFastEvaluationStrategy(),
        DomainStrategy.Priority   => new PriorityOrderedEvaluationStrategy(),
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unknown EvaluationStrategy.")
    };
}
