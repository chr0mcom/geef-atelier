using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence;

/// <summary>
/// Keeps profiles, intake shadows and the intake columns of orders out of the EF model until migration Step49 and its entity
/// configurations exist, so the model keeps matching the Step48 schema. Removed together with the call in
/// <see cref="AtelierDbContext"/> once the configurations are added.
/// </summary>
internal static class IntakeModelPlaceholderExtensions
{
    /// <summary>Ignores <see cref="UserProfileEntity"/>, <see cref="OrderIntakeShadowEntity"/> and the six intake columns of <see cref="OrderEntity"/>.</summary>
    public static ModelBuilder IgnoreIntakeUntilStep49(this ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<UserProfileEntity>();
        modelBuilder.Ignore<OrderIntakeShadowEntity>();
        modelBuilder.Entity<OrderEntity>()
            .Ignore(o => o.IntakeOutcome)
            .Ignore(o => o.IntakeAttempts)
            .Ignore(o => o.IntakeStartedAt)
            .Ignore(o => o.IntakeFinishedAt)
            .Ignore(o => o.QuestionsAskedAt)
            .Ignore(o => o.AnsweredAt);
        return modelBuilder;
    }
}
