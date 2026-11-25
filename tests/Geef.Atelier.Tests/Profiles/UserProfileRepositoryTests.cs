using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Infrastructure.Persistence.Profiles;
using Geef.Atelier.Tests.Persistence;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Profiles;

/// <summary><see cref="UserProfileRepository"/> against real Postgres.</summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class UserProfileRepositoryTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Upsert_inserts_then_replaces_and_keeps_the_creation_time()
    {
        var user = NewUser();
        await using (var db = fixture.NewContext())
            await new UserProfileRepository(db).UpsertAsync(new UserProfileEntity
            {
                Username = user, DisplayName = "Anna Beispiel", Formality = Formality.Formal, Language = "de", CreatedAt = T0, UpdatedAt = T0,
            });

        await using (var db = fixture.NewContext())
            await new UserProfileRepository(db).UpsertAsync(new UserProfileEntity
            {
                Username = user, Role = "Teamleitung", Signature = "Viele Grüße\nAnna", CreatedAt = T0.AddDays(3), UpdatedAt = T0.AddDays(3),
            });

        await using var check = fixture.NewContext();
        var stored = await new UserProfileRepository(check).GetAsync(user);
        Assert.NotNull(stored);
        Assert.Null(stored.DisplayName);
        Assert.Equal("Teamleitung", stored.Role);
        Assert.Equal(Formality.Unset, stored.Formality);
        Assert.Null(stored.Language);
        Assert.Equal("Viele Grüße\nAnna", stored.Signature);
        Assert.Equal(T0, stored.CreatedAt);
        Assert.Equal(T0.AddDays(3), stored.UpdatedAt);
    }

    [Fact]
    public async Task Profiles_are_strictly_per_user_and_delete_reports_whether_one_existed()
    {
        var owner = NewUser();
        var other = NewUser();
        await using var db = fixture.NewContext();
        var repository = new UserProfileRepository(db);
        await repository.UpsertAsync(new UserProfileEntity { Username = owner, NoGos = "Keine Floskeln", CreatedAt = T0, UpdatedAt = T0 });

        Assert.Null(await repository.GetAsync(other));
        Assert.False(await repository.DeleteAsync(other));
        Assert.NotNull(await repository.GetAsync(owner));
        Assert.True(await repository.DeleteAsync(owner));
        Assert.Null(await repository.GetAsync(owner));
        Assert.False(await repository.DeleteAsync(owner));
    }
}
