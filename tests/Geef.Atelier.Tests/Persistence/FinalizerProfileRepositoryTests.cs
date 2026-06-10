using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class FinalizerProfileRepositoryTests(PostgresFixture db)
{
    private static FinalizerProfile Custom(string name) => new(
        Name: name,
        DisplayName: name + " display",
        Description: "test finalizer",
        FinalizerType: FinalizerType.FileExport,
        Settings: new Dictionary<string, string> { [FileExportSettings.KeyFormat] = "markdown" },
        IsSystem: false,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Create_ThenGetByName_ReturnsProfile()
    {
        await using var ctx = db.NewContext();
        var repo = new FinalizerProfileRepository(ctx);

        var profile = await repo.CreateAsync(Custom("custom-repo-test-1"), default);
        var retrieved = await repo.GetByNameAsync("custom-repo-test-1", default);

        Assert.NotNull(retrieved);
        Assert.Equal("custom-repo-test-1", retrieved.Name);
        Assert.Equal(FinalizerType.FileExport, retrieved.FinalizerType);
    }

    [Fact]
    public async Task List_IncludesCreatedProfile()
    {
        await using var ctx = db.NewContext();
        var repo = new FinalizerProfileRepository(ctx);

        await repo.CreateAsync(Custom("custom-list-test-1"), default);
        var all = await repo.ListAsync(default);

        Assert.Contains(all, p => p.Name == "custom-list-test-1");
    }

    [Fact]
    public async Task Update_ChangesDescription()
    {
        await using var ctx = db.NewContext();
        var repo = new FinalizerProfileRepository(ctx);

        await repo.CreateAsync(Custom("custom-update-test-1"), default);
        var updated = Custom("custom-update-test-1") with { Description = "updated description" };
        await repo.UpdateAsync(updated, default);

        await using var verify = db.NewContext();
        var retrieved = await verify.FinalizerProfiles.AsNoTracking()
            .FirstAsync(p => p.Name == "custom-update-test-1");
        Assert.Equal("updated description", retrieved.Description);
    }

    [Fact]
    public async Task Delete_RemovesProfile()
    {
        await using var ctx = db.NewContext();
        var repo = new FinalizerProfileRepository(ctx);

        await repo.CreateAsync(Custom("custom-delete-test-1"), default);
        await repo.DeleteAsync("custom-delete-test-1", default);

        await using var verify = db.NewContext();
        var retrieved = await verify.FinalizerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == "custom-delete-test-1");
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task Rename_UpdatesNameAndCascadesIntoTemplate()
    {
        await using var ctx = db.NewContext();
        var repo = new FinalizerProfileRepository(ctx);
        var tplRepo = new CrewTemplateRepository(ctx);

        await repo.CreateAsync(Custom("custom-fin-rename-a"), default);
        var template = new CrewTemplate(
            Name: "custom-fin-rename-tpl",
            DisplayName: "Rename Test",
            Description: "desc",
            ExecutorProfileName: "default-executor",
            ReviewerProfileNames: [],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            AdvisorProfileNames: [],
            GroundingProviderNames: [],
            IsSystem: false,
            FinalizerProfileNames: ["custom-fin-rename-a"],
            RunFinalizersOnMaxAttempts: false);
        await tplRepo.CreateAsync(template, default);

        await repo.RenameAsync("custom-fin-rename-a", "custom-fin-rename-b", default);

        await using var verify = db.NewContext();
        Assert.Null(await verify.FinalizerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == "custom-fin-rename-a"));
        Assert.NotNull(await verify.FinalizerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == "custom-fin-rename-b"));

        var tpl = await verify.CrewTemplates.AsNoTracking()
            .FirstAsync(t => t.Name == "custom-fin-rename-tpl");
        Assert.Contains("custom-fin-rename-b", tpl.FinalizerProfileNames);
        Assert.DoesNotContain("custom-fin-rename-a", tpl.FinalizerProfileNames);
    }

    [Fact]
    public async Task SystemFinalizerProfiles_AreSeeded_ListIsNonEmpty()
    {
        await using var ctx = db.NewContext();
        var all = await ctx.FinalizerProfiles.AsNoTracking()
            .Where(p => p.IsSystem)
            .CountAsync();

        // 17 from Step22 + 2 from Step30 (D-054): learning-extractor + learning-publisher
        Assert.Equal(19, all);
    }
}
