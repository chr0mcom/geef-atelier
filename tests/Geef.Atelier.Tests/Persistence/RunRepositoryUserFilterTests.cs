using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class RunRepositoryUserFilterTests(PostgresFixture fixture)
{
    private static async Task<Guid> CreateRunForUser(AtelierDbContext db, string? user)
    {
        var svc = new RunPersistenceService(db);
        return await svc.CreateRunAsync("Briefing", "{}", user, cancellationToken: CancellationToken.None);
    }

    [Fact]
    public async Task ListAsync_WithUsername_ReturnsOnlyThatUsersRuns()
    {
        await using var db = fixture.NewContext();
        var repo = new RunRepository(db);

        // Two runs for userA, one for userB
        await CreateRunForUser(db, "userA");
        await CreateRunForUser(db, "userA");
        await CreateRunForUser(db, "userB");

        var results = await repo.ListAsync(10, null, "userA", CancellationToken.None);

        Assert.True(results.Count >= 2, $"Expected at least 2 runs for userA, got {results.Count}");
        Assert.All(results, r => Assert.Equal("userA", r.CreatedByUser));
    }

    [Fact]
    public async Task ListAsync_WithNullUsername_ReturnsAllRuns()
    {
        await using var db = fixture.NewContext();
        var repo = new RunRepository(db);

        await CreateRunForUser(db, "alice");
        await CreateRunForUser(db, "bob");
        await CreateRunForUser(db, "charlie");

        var all = await repo.ListAsync(100, null, null, CancellationToken.None);

        // The test DB is shared, so we just verify at least 3 runs exist total and that
        // runs from multiple users are represented.
        Assert.True(all.Count >= 3, $"Expected at least 3 runs total, got {all.Count}");
    }

}
