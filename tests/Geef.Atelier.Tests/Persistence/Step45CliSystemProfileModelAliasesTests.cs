using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Runs the Step45 statements against a real database, so the scope of the update is verified as
/// written rather than as intended: only seeded system profiles on the claude-cli provider may move,
/// the mapping must stay inside its model family, and everything else must be left alone.
/// </summary>
[Collection("Postgres")]
public sealed class Step45CliSystemProfileModelAliasesTests(PostgresFixture fixture)
{
    [Fact]
    public async Task LiftsSystemClaudeCliProfilesOntoTheirFamilyAlias_AndLeavesEverythingElseAlone()
    {
        await using var db = fixture.NewContext();

        var systemOpus       = await SeedReviewerAsync(db, "s45-sys-opus",     "claude-cli", "claude-opus-4-8",           isSystem: true);
        var systemSonnet     = await SeedReviewerAsync(db, "s45-sys-sonnet",   "claude-cli", "claude-sonnet-5",           isSystem: true);
        var systemHaikuDot   = await SeedReviewerAsync(db, "s45-sys-haiku",    "claude-cli", "claude-haiku-4.5",          isSystem: true);
        var systemOnRouter   = await SeedReviewerAsync(db, "s45-sys-router",   "openrouter", "anthropic/claude-opus-4.8", isSystem: true);
        var systemPrefixed   = await SeedReviewerAsync(db, "s45-sys-prefixed", "claude-cli", "anthropic/claude-opus-4.8", isSystem: true);
        var userOpus         = await SeedReviewerAsync(db, "s45-user-opus",    "claude-cli", "claude-opus-4-8",           isSystem: false);
        var systemAlreadyNew = await SeedReviewerAsync(db, "s45-sys-alias",    "claude-cli", "claude-opus-latest",        isSystem: true);

        foreach (var sql in ExtractUpSqlStatements())
            await db.Database.ExecuteSqlRawAsync(sql);

        await using var verify = fixture.NewContext();

        Assert.Equal("claude-opus-latest", await ModelOfAsync(verify, systemOpus));
        Assert.Equal("claude-sonnet-latest", await ModelOfAsync(verify, systemSonnet));
        Assert.Equal("claude-haiku-latest", await ModelOfAsync(verify, systemHaikuDot));

        Assert.Equal("anthropic/claude-opus-4.8", await ModelOfAsync(verify, systemOnRouter));

        Assert.Equal("claude-opus-latest", await ModelOfAsync(verify, systemPrefixed));

        Assert.Equal("claude-opus-4-8", await ModelOfAsync(verify, userOpus));

        Assert.Equal("claude-opus-latest", await ModelOfAsync(verify, systemAlreadyNew));
    }

    [Fact]
    public async Task IsIdempotent()
    {
        await using var db = fixture.NewContext();
        var name = await SeedReviewerAsync(db, "s45-idem", "claude-cli", "claude-opus-4-8", isSystem: true);

        var statements = ExtractUpSqlStatements();
        foreach (var sql in statements) await db.Database.ExecuteSqlRawAsync(sql);
        foreach (var sql in statements) await db.Database.ExecuteSqlRawAsync(sql);

        await using var verify = fixture.NewContext();
        Assert.Equal("claude-opus-latest", await ModelOfAsync(verify, name));
    }

    [Fact]
    public void DownIsADocumentedNoOp()
    {
        Assert.Empty(new Step45CliSystemProfileModelAliases().DownOperations);
    }

    [Fact]
    public void EveryStatementIsScopedToSystemProfilesOnClaudeCli()
    {
        foreach (var sql in ExtractUpSqlStatements())
        {
            Assert.Contains("\"IsSystem\" = true", sql);
            Assert.Contains("\"Provider\" = 'claude-cli'", sql);
            Assert.DoesNotContain("LIKE", sql);
        }
    }

    private static IReadOnlyList<string> ExtractUpSqlStatements()
    {
        var statements = new Step45CliSystemProfileModelAliases()
            .UpOperations.OfType<SqlOperation>().Select(o => o.Sql).ToList();

        Assert.Equal(9, statements.Count);
        return statements;
    }

    private static async Task<string> SeedReviewerAsync(
        AtelierDbContext db, string name, string provider, string model, bool isSystem)
    {
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "ReviewerProfiles" ("Name","DisplayName","Description","SystemPrompt","Provider","Model","MaxTokens","IsSystem","ToolNames")
            VALUES ({0},{0},'step45 seed','p',{1},{2},1024,{3},'[]'::jsonb)
            ON CONFLICT ("Name") DO UPDATE SET "Provider" = EXCLUDED."Provider", "Model" = EXCLUDED."Model", "IsSystem" = EXCLUDED."IsSystem";
            """.Replace("{0}", $"'{name}'").Replace("{1}", $"'{provider}'")
               .Replace("{2}", $"'{model}'").Replace("{3}", isSystem ? "true" : "false"));

        return name;
    }

    private static async Task<string> ModelOfAsync(AtelierDbContext db, string name)
    {
        var profile = await db.Set<ReviewerProfile>().AsNoTracking().SingleAsync(p => p.Name == name);
        return profile.Model;
    }
}
