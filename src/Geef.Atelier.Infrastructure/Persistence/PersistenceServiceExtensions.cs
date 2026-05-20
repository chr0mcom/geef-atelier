using Geef.Atelier.Application.Dashboard;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.OAuth;
using Geef.Atelier.Core.Persistence.Providers;
using Geef.Atelier.Core.Persistence.TemplateStudio;
using Geef.Atelier.Infrastructure.Dashboard;
using Geef.Atelier.Infrastructure.Persistence.Crew;
using Geef.Atelier.Infrastructure.Persistence.Dashboard;
using Geef.Atelier.Infrastructure.Persistence.OAuth;
using Geef.Atelier.Infrastructure.Persistence.Providers;
using Geef.Atelier.Infrastructure.Persistence.TemplateStudio;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Persistence;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddAtelierPersistence(this IServiceCollection services)
    {
        services.AddScoped<IAtelierUserRepository, AtelierUserRepository>();
        services.AddScoped<IRunPersistenceService, RunPersistenceService>();
        services.AddScoped<IRunRepository, RunRepository>();
        services.AddScoped<IReviewerProfileRepository, ReviewerProfileRepository>();
        services.AddScoped<IExecutorProfileRepository, ExecutorProfileRepository>();
        services.AddScoped<ICrewTemplateRepository, CrewTemplateRepository>();
        services.AddScoped<IAdvisorProfileRepository, AdvisorProfileRepository>();
        services.AddScoped<IAdvisorConsultationRepository, AdvisorConsultationRepository>();
        services.AddScoped<IGroundingProviderProfileRepository, GroundingProviderProfileRepository>();
        services.AddScoped<IGroundingConsultationRepository, GroundingConsultationRepository>();
        services.AddScoped<IGroundingActorCostRepository, GroundingActorCostRepository>();
        services.AddScoped<IFinalizerProfileRepository, FinalizerProfileRepository>();
        services.AddScoped<IProviderRepository, ProviderRepository>();
        services.AddScoped<IRunArtifactRepository, RunArtifactRepository>();
        services.AddScoped<ITemplateStudioAnalysisRepository, TemplateStudioAnalysisRepository>();
        services.AddScoped<IOAuthClientRepository, OAuthClientRepository>();
        services.AddScoped<IOAuthAuthorizationCodeRepository, OAuthAuthorizationCodeRepository>();
        services.AddScoped<IOAuthAccessTokenRepository, OAuthAccessTokenRepository>();
        services.AddScoped<IOAuthRefreshTokenRepository, OAuthRefreshTokenRepository>();
        services.AddScoped<IOAuthAuditLogRepository, OAuthAuditLogRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddMemoryCache();
        services.AddSingleton<IDashboardService, DashboardService>();
        return services;
    }
}
