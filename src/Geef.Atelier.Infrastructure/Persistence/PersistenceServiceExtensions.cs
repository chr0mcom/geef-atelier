using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Persistence.Crew;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Persistence;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddAtelierPersistence(this IServiceCollection services)
    {
        services.AddScoped<IRunPersistenceService, RunPersistenceService>();
        services.AddScoped<IRunRepository, RunRepository>();
        services.AddScoped<IReviewerProfileRepository, ReviewerProfileRepository>();
        services.AddScoped<IExecutorProfileRepository, ExecutorProfileRepository>();
        services.AddScoped<ICrewTemplateRepository, CrewTemplateRepository>();
        services.AddScoped<IAdvisorProfileRepository, AdvisorProfileRepository>();
        services.AddScoped<IAdvisorConsultationRepository, AdvisorConsultationRepository>();
        services.AddScoped<IGroundingProviderProfileRepository, GroundingProviderProfileRepository>();
        services.AddScoped<IGroundingConsultationRepository, GroundingConsultationRepository>();
        return services;
    }
}
