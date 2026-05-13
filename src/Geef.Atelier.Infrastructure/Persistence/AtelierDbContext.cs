using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtelierDbContext).Assembly);
}
