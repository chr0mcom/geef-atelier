using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Persistence.Providers;
using Geef.Atelier.Infrastructure.Persistence.SiteSettings;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>Primary EF Core database context for Geef.Atelier.</summary>
public sealed class AtelierDbContext(DbContextOptions<AtelierDbContext> options) : DbContext(options)
{
    public DbSet<RunEntity> Runs => Set<RunEntity>();
    public DbSet<IterationEntity> Iterations => Set<IterationEntity>();
    public DbSet<FindingEntity> Findings => Set<FindingEntity>();
    public DbSet<EventEntity> Events => Set<EventEntity>();
    public DbSet<RunWarningEntity> RunWarnings => Set<RunWarningEntity>();

    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<SourceEntity> Sources => Set<SourceEntity>();
    public DbSet<OrderSourceEntity> OrderSources => Set<OrderSourceEntity>();
    public DbSet<RunSourceUsageEntity> RunSourceUsages => Set<RunSourceUsageEntity>();
    public DbSet<UploadTicketEntity> UploadTickets => Set<UploadTicketEntity>();
    public DbSet<SourceRefusalEntity> SourceRefusals => Set<SourceRefusalEntity>();

    public DbSet<UserProfileEntity> UserProfiles => Set<UserProfileEntity>();
    public DbSet<OrderIntakeShadowEntity> OrderIntakeShadows => Set<OrderIntakeShadowEntity>();

    public DbSet<IterationActorCostEntity> IterationActorCosts => Set<IterationActorCostEntity>();

    internal DbSet<AtelierUser> Users => Set<AtelierUser>();

    internal DbSet<ProviderEntity> Providers => Set<ProviderEntity>();

    internal DbSet<SiteSettingsEntity> SiteSettings => Set<SiteSettingsEntity>();

    internal DbSet<OAuthClient> OAuthClients => Set<OAuthClient>();
    internal DbSet<OAuthAuthorizationCode> OAuthAuthorizationCodes => Set<OAuthAuthorizationCode>();
    internal DbSet<OAuthAccessToken> OAuthAccessTokens => Set<OAuthAccessToken>();
    internal DbSet<OAuthRefreshToken> OAuthRefreshTokens => Set<OAuthRefreshToken>();
    internal DbSet<OAuthAuditLogEntry> OAuthAuditLog => Set<OAuthAuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtelierDbContext).Assembly);
    }
}
