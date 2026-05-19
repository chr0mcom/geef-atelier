using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class RunArtifactRepositoryTests(PostgresFixture db)
{
    private static RunEntity SeedRun(Guid id) => new()
    {
        Id = id,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = RunStatus.Completed,
        BriefingText = "artifact test run",
        ConfigJson = "{}",
    };

    private static RunArtifact FileArtifact(Guid runId, string profName, string filename) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        FinalizerProfileName = profName,
        ArtifactType = ArtifactType.File,
        Filename = filename,
        ContentType = "text/markdown",
        SizeBytes = 42,
        StorageUri = $"/app/exports/{runId:N}/{filename}",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Create_ThenListByRun_ReturnsArtifact()
    {
        await using var ctx = db.NewContext();
        var runId = Guid.NewGuid();
        ctx.Runs.Add(SeedRun(runId));
        await ctx.SaveChangesAsync();

        var repo = new RunArtifactRepository(ctx);
        await repo.CreateAsync(FileArtifact(runId, "export-markdown", "doc.md"), default);

        var list = await repo.ListByRunAsync(runId, default);
        Assert.Single(list);
        Assert.Equal("doc.md", list[0].Filename);
    }

    [Fact]
    public async Task GetById_ReturnsCorrectArtifact()
    {
        await using var ctx = db.NewContext();
        var runId = Guid.NewGuid();
        ctx.Runs.Add(SeedRun(runId));
        await ctx.SaveChangesAsync();

        var artifact = FileArtifact(runId, "export-html", "doc.html");
        var repo = new RunArtifactRepository(ctx);
        await repo.CreateAsync(artifact, default);

        var retrieved = await repo.GetByIdAsync(artifact.Id, default);
        Assert.NotNull(retrieved);
        Assert.Equal(artifact.Id, retrieved.Id);
        Assert.Equal("doc.html", retrieved.Filename);
    }

    [Fact]
    public async Task DeleteByRun_RemovesAllArtifactsForRun()
    {
        await using var ctx = db.NewContext();
        var runId = Guid.NewGuid();
        ctx.Runs.Add(SeedRun(runId));
        await ctx.SaveChangesAsync();

        var repo = new RunArtifactRepository(ctx);
        await repo.CreateAsync(FileArtifact(runId, "export-markdown", "a.md"), default);
        await repo.CreateAsync(FileArtifact(runId, "export-html", "a.html"), default);

        await repo.DeleteByRunAsync(runId, default);

        var remaining = await repo.ListByRunAsync(runId, default);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task DeletingRun_CascadesDeleteToRunArtifacts()
    {
        await using var ctx = db.NewContext();
        var runId = Guid.NewGuid();
        ctx.Runs.Add(SeedRun(runId));
        await ctx.SaveChangesAsync();

        var artifact = FileArtifact(runId, "export-markdown", "cascade.md");
        ctx.Entry(artifact).State = EntityState.Added;
        await ctx.SaveChangesAsync();

        // Verify artifact exists
        await using var checkCtx = db.NewContext();
        Assert.NotNull(await checkCtx.RunArtifacts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == artifact.Id));

        // Delete the run — DB CASCADE should remove artifact
        await using var deleteCtx = db.NewContext();
        var run = await deleteCtx.Runs.FirstAsync(r => r.Id == runId);
        deleteCtx.Runs.Remove(run);
        await deleteCtx.SaveChangesAsync();

        await using var verifyCtx = db.NewContext();
        Assert.Null(await verifyCtx.RunArtifacts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == artifact.Id));
    }
}
