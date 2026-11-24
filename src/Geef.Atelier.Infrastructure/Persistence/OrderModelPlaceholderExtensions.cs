using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>
/// Keeps orders and sources out of the EF model until migration Step48 and its entity configurations exist, so the
/// model keeps matching the Step47 schema. Removed together with the call in <see cref="AtelierDbContext"/> once the
/// configurations are added.
/// </summary>
internal static class OrderModelPlaceholderExtensions
{
    /// <summary>Ignores the order and source entities and the order columns of <see cref="RunEntity"/>.</summary>
    public static ModelBuilder IgnoreOrdersUntilStep48(this ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<OrderEntity>();
        modelBuilder.Ignore<SourceEntity>();
        modelBuilder.Ignore<OrderSourceEntity>();
        modelBuilder.Ignore<RunSourceUsageEntity>();
        modelBuilder.Entity<RunEntity>()
            .Ignore(r => r.OrderId)
            .Ignore(r => r.OrderVersion);
        return modelBuilder;
    }
}
