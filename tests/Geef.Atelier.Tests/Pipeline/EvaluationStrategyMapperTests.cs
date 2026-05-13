using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk.Policies;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class EvaluationStrategyMapperTests
{
    [Theory]
    [InlineData(EvaluationStrategy.Parallel,   typeof(ParallelEvaluationStrategy))]
    [InlineData(EvaluationStrategy.Sequential, typeof(SequentialEvaluationStrategy))]
    [InlineData(EvaluationStrategy.FailFast,   typeof(FailFastEvaluationStrategy))]
    [InlineData(EvaluationStrategy.Priority,   typeof(PriorityOrderedEvaluationStrategy))]
    public void Map_ReturnsCorrectStrategyType(EvaluationStrategy strategy, Type expectedType)
    {
        var result = EvaluationStrategyMapper.Map(strategy);
        Assert.IsType(expectedType, result);
    }

    [Fact]
    public void Map_InvalidStrategy_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EvaluationStrategyMapper.Map((EvaluationStrategy)99));
    }
}
