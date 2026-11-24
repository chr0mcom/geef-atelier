using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Configuration;

/// <summary>The one registration of <see cref="LearningOptions"/> (the <c>Learning:Enabled</c> switch).</summary>
public static class LearningSwitchServiceExtensions
{
    /// <summary>
    /// Binds <see cref="LearningOptions"/> to the <c>Learning</c> section. Every reader (finalizers, the retrieval
    /// provider and the learnings page) reads it through <c>IOptionsMonitor</c>, so a change reaches all of them alike.
    /// </summary>
    public static IServiceCollection AddLearningSwitch(this IServiceCollection services, IConfiguration configuration) =>
        services.Configure<LearningOptions>(configuration.GetSection(LearningOptions.SectionName));
}
