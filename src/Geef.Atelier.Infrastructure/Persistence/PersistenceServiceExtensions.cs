using Geef.Atelier.Application.Dashboard;
using Geef.Atelier.Application.SiteSettings;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Mcp;
using Geef.Atelier.Core.Persistence.OAuth;
using Geef.Atelier.Core.Persistence.Intake;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Profiles;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Core.Persistence.Providers;
using Geef.Atelier.Core.Persistence.TemplateStudio;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.SiteSettings;
using Geef.Atelier.Infrastructure.Dashboard;
using Geef.Atelier.Infrastructure.Persistence.Crew;
using Geef.Atelier.Infrastructure.Persistence.Crew.Learning;
using Geef.Atelier.Infrastructure.Persistence.Dashboard;
using Geef.Atelier.Infrastructure.Persistence.OAuth;
using Geef.Atelier.Infrastructure.Persistence.Intake;
using Geef.Atelier.Infrastructure.Persistence.Orders;
using Geef.Atelier.Infrastructure.Persistence.Profiles;
using Geef.Atelier.Infrastructure.Persistence.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Infrastructure.Persistence.Providers;
using Geef.Atelier.Infrastructure.Persistence.Repositories;
using Geef.Atelier.Infrastructure.Persistence.SiteSettings;
using Geef.Atelier.Infrastructure.Persistence.StudioSettings;
using Geef.Atelier.Infrastructure.Persistence.TemplateStudio;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Persistence;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddAtelierPersistence(this IServiceCollection services)
    {
        services.AddScoped<IAtelierUserRepository, AtelierUserRepository>();
        services.AddScoped<IAccountStore, AccountStore>();
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
        services.AddScoped<ILearningRepository, LearningRepository>();
        services.AddScoped<ISiteSettingsRepository, SiteSettingsRepository>();
        services.AddScoped<ISiteSettingsService, SiteSettingsService>();
        services.AddScoped<IStudioSettingsRepository, StudioSettingsRepository>();
        services.AddScoped<IToolDefinitionRepository, ToolDefinitionRepository>();
        services.AddScoped<IToolInvocationRepository, ToolInvocationRepository>();
        services.AddScoped<IMcpServerConfigRepository, McpServerConfigRepository>();
        services.AddScoped<ISpecializationPackRepository, SpecializationPackRepository>();
        services.AddScoped<IRunWarningRepository, RunWarningRepository>();
        services.AddSingleton<IRunWarningSink, RunWarningSink>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IIntakeShadowRepository, IntakeShadowRepository>();
        services.AddScoped<SourceRepository>();
        services.AddScoped<ISourceRepository>(sp => sp.GetRequiredService<SourceRepository>());
        services.AddScoped<ISourceRetentionStore>(sp => sp.GetRequiredService<SourceRepository>());
        services.AddSingleton<IRunReportWriter, RunReportWriter>();
        services.AddMemoryCache();
        services.AddSingleton<IDashboardService, DashboardService>();
        return services;
    }
}
