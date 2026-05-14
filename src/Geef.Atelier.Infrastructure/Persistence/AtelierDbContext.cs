using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
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

    public DbSet<IterationActorCostEntity> IterationActorCosts => Set<IterationActorCostEntity>();

    internal DbSet<KnowledgeDocumentEntity> KnowledgeDocuments => Set<KnowledgeDocumentEntity>();
    internal DbSet<KnowledgeDocumentChunkEntity> KnowledgeDocumentChunks => Set<KnowledgeDocumentChunkEntity>();

    internal DbSet<TemplateStudioAnalysisEntity> TemplateStudioAnalyses => Set<TemplateStudioAnalysisEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtelierDbContext).Assembly);
}
