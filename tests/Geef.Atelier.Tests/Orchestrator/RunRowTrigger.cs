using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.Orchestrator;

/// <summary>
/// A <c>BEFORE UPDATE</c> row trigger on <c>Runs</c> that only fires for the run with the given briefing, so a test can
/// delay or fail one database write of its own run without touching runs of parallel tests. Dropped on dispose.
/// </summary>
internal sealed class RunRowTrigger(PostgresFixture fixture, string name) : IAsyncDisposable
{
    /// <summary>Creates the trigger; <paramref name="condition"/> and <paramref name="body"/> are PL/pgSQL.</summary>
    public static async Task<RunRowTrigger> CreateAsync(PostgresFixture fixture, string briefing, string condition, string body)
    {
        var name = $"test_outcome_{Guid.NewGuid():N}";
        var literal = briefing.Replace("'", "''");
        var sql = $$"""
            CREATE FUNCTION {{name}}() RETURNS trigger LANGUAGE plpgsql AS $fn$ BEGIN {{body}} END $fn$;
            CREATE TRIGGER {{name}} BEFORE UPDATE ON "Runs" FOR EACH ROW
                WHEN (NEW."BriefingText" = '{{literal}}' AND {{condition}}) EXECUTE FUNCTION {{name}}();
            """;
        await using var db = fixture.NewContext();
        await db.Database.ExecuteSqlRawAsync(sql);
        return new RunRowTrigger(fixture, name);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        var sql = $"""
            DROP TRIGGER IF EXISTS {name} ON "Runs";
            DROP FUNCTION IF EXISTS {name}();
            """;
        await using var db = fixture.NewContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }
}
