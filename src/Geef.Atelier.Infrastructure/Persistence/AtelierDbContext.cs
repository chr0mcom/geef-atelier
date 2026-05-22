using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Geef.Atelier.Infrastructure.Persistence.Crew.Learning;
using Geef.Atelier.Infrastructure.Persistence.Providers;
using Geef.Atelier.Infrastructure.Persistence.SiteSettings;
using Geef.Atelier.Infrastructure.Persistence.TemplateStudio;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>Primary EF Core database context for Geef.Atelier.</summary>
public sealed class AtelierDbContext(DbContextOptions<AtelierDbContext> options) : DbContext(options)
{
    public DbSet<RunEntity> Runs => Set<RunEntity>();
    public DbSet<IterationEntity> Iterations => Set<IterationEntity>();
    public DbSet<FindingEntity> Findings => Set<FindingEntity>();
    public DbSet<EventEntity> Events => Set<EventEntity>();

    public DbSet<ReviewerProfile> ReviewerProfiles => Set<ReviewerProfile>();
    public DbSet<ExecutorProfile> ExecutorProfiles => Set<ExecutorProfile>();
    public DbSet<CrewTemplate> CrewTemplates => Set<CrewTemplate>();
    public DbSet<AdvisorProfile> AdvisorProfiles => Set<AdvisorProfile>();
    public DbSet<AdvisorConsultation> AdvisorConsultations => Set<AdvisorConsultation>();

    public DbSet<GroundingProviderProfile> GroundingProviderProfiles => Set<GroundingProviderProfile>();
    public DbSet<GroundingConsultation> GroundingConsultations => Set<GroundingConsultation>();
    public DbSet<GroundingActorCost> GroundingActorCosts => Set<GroundingActorCost>();

    public DbSet<FinalizerProfile> FinalizerProfiles => Set<FinalizerProfile>();
    public DbSet<RunArtifact> RunArtifacts => Set<RunArtifact>();
    public DbSet<FinalizationActorCost> FinalizationActorCosts => Set<FinalizationActorCost>();

    public DbSet<IterationActorCostEntity> IterationActorCosts => Set<IterationActorCostEntity>();

    internal DbSet<KnowledgeDocumentEntity> KnowledgeDocuments => Set<KnowledgeDocumentEntity>();
    internal DbSet<KnowledgeDocumentChunkEntity> KnowledgeDocumentChunks => Set<KnowledgeDocumentChunkEntity>();

    internal DbSet<LearningEntryEntity> LearningEntries => Set<LearningEntryEntity>();

    internal DbSet<TemplateStudioAnalysisEntity> TemplateStudioAnalyses => Set<TemplateStudioAnalysisEntity>();

    internal DbSet<AtelierUser> Users => Set<AtelierUser>();

    internal DbSet<ProviderEntity> Providers => Set<ProviderEntity>();

    internal DbSet<SiteSettingsEntity> SiteSettings => Set<SiteSettingsEntity>();

    internal DbSet<OAuthClient> OAuthClients => Set<OAuthClient>();
    internal DbSet<OAuthAuthorizationCode> OAuthAuthorizationCodes => Set<OAuthAuthorizationCode>();
    internal DbSet<OAuthAccessToken> OAuthAccessTokens => Set<OAuthAccessToken>();
    internal DbSet<OAuthRefreshToken> OAuthRefreshTokens => Set<OAuthRefreshToken>();
    internal DbSet<OAuthAuditLogEntry> OAuthAuditLog => Set<OAuthAuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtelierDbContext).Assembly);
}
