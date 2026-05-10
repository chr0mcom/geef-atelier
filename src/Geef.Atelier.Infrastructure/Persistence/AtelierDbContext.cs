using Geef.Atelier.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>Primary EF Core database context for Geef.Atelier.</summary>
public sealed class AtelierDbContext(DbContextOptions<AtelierDbContext> options) : DbContext(options)
{
    public DbSet<RunEntity> Runs => Set<RunEntity>();
    public DbSet<IterationEntity> Iterations => Set<IterationEntity>();
    public DbSet<FindingEntity> Findings => Set<FindingEntity>();
    public DbSet<EventEntity> Events => Set<EventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtelierDbContext).Assembly);
}
