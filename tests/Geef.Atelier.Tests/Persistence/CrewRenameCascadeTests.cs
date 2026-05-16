using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Crew;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// Custom profile/template slugs are renameable. Because the by-name references that templates
/// and runs hold are plain string / JSONB-list columns (no DB-level foreign keys), the rename
/// must cascade in code. These tests pin that cascade and the service-level guards.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CrewRenameCascadeTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var setup = NewContext();
        await setup.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AtelierDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new AtelierDbContext(options);
    }

    private static ExecutorProfile Exec(string name) =>
        new(name, name, "desc", "prompt", "openrouter", "model", null, false);

    private static ReviewerProfile Rev(string name) =>
        new(name, name, "desc", "prompt", "openrouter", "model", null, false);

    private static CrewTemplate Template(string name, string executor, IReadOnlyList<string> reviewers) =>
        new(Name: name,
            DisplayName: name,
            Description: "t",
            ExecutorProfileName: executor,
            ReviewerProfileNames: reviewers,
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            AdvisorProfileNames: [],
            GroundingProviderNames: [],
            IsSystem: false);

    [Fact]
    public async Task ExecutorRename_CascadesIntoCustomTemplateExecutorRef()
    {
        await using var ctx = NewContext();
        var execRepo = new ExecutorProfileRepository(ctx);
        var tplRepo  = new CrewTemplateRepository(ctx);

        await execRepo.CreateAsync(Exec("custom-exec-a"));
        await tplRepo.CreateAsync(Template("custom-tpl", "custom-exec-a", ["briefing-fidelity"]));

        await execRepo.RenameAsync("custom-exec-a", "custom-exec-b");

        await using var verify = NewContext();
        Assert.Null(await verify.ExecutorProfiles.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Name == "custom-exec-a"));
        Assert.NotNull(await verify.ExecutorProfiles.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Name == "custom-exec-b"));
        var tpl = await verify.CrewTemplates.AsNoTracking().FirstAsync(t => t.Name == "custom-tpl");
        Assert.Equal("custom-exec-b", tpl.ExecutorProfileName);
    }

    [Fact]
    public async Task ReviewerRename_CascadesIntoTemplateListPreservingOrderAndOtherEntries()
    {
        await using var ctx = NewContext();
        var revRepo = new ReviewerProfileRepository(ctx);
        var tplRepo = new CrewTemplateRepository(ctx);

        await revRepo.CreateAsync(Rev("custom-rev-a"));
        await tplRepo.CreateAsync(
            Template("custom-tpl-r", "default-executor", ["briefing-fidelity", "custom-rev-a"]));

        await revRepo.RenameAsync("custom-rev-a", "custom-rev-b");

        await using var verify = NewContext();
        var tpl = await verify.CrewTemplates.AsNoTracking().FirstAsync(t => t.Name == "custom-tpl-r");
        Assert.Equal(["briefing-fidelity", "custom-rev-b"], tpl.ReviewerProfileNames);
    }

    [Fact]
    public async Task TemplateRename_CascadesToRunTemplateName_ButLeavesSnapshotFrozen()
    {
        const string frozenSnapshot = """{"templateName":"custom-old","schemaVersion":1}""";

        await using var ctx = NewContext();
        var tplRepo = new CrewTemplateRepository(ctx);
        await tplRepo.CreateAsync(Template("custom-old", "default-executor", ["briefing-fidelity"]));

        var runId = Guid.NewGuid();
        ctx.Runs.Add(new RunEntity
        {
            Id = runId,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Completed,
            BriefingText = "b",
            ConfigJson = "{}",
            CrewTemplateName = "custom-old",
            CrewSnapshot = frozenSnapshot,
        });
        await ctx.SaveChangesAsync();

        await tplRepo.RenameAsync("custom-old", "custom-new");

        await using var verify = NewContext();
        Assert.NotNull(await verify.CrewTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Name == "custom-new"));
        var run = await verify.Runs.AsNoTracking().FirstAsync(r => r.Id == runId);
        Assert.Equal("custom-new", run.CrewTemplateName);
        // The frozen snapshot must NOT be rewritten by the cascade: it still names the
        // old template so the run stays reproducible. (Postgres jsonb normalises whitespace,
        // so assert on content rather than an exact byte match.)
        Assert.Contains("custom-old", run.CrewSnapshot);
        Assert.DoesNotContain("custom-new", run.CrewSnapshot);
    }

    [Fact]
    public async Task RenameAsync_NonExistentProfile_ThrowsClearError()
    {
        await using var ctx = NewContext();
        var execRepo = new ExecutorProfileRepository(ctx);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => execRepo.RenameAsync("custom-missing", "custom-whatever"));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Service_RenameCustomExecutor_EnforcesPrefixAndCascades()
    {
        await using var ctx = NewContext();
        var svc = new CrewService(
            new ReviewerProfileRepository(ctx),
            new ExecutorProfileRepository(ctx),
            new AdvisorProfileRepository(ctx),
            new GroundingProviderProfileRepository(ctx),
            new CrewTemplateRepository(ctx));

        await svc.CreateCustomExecutorProfileAsync(Exec("svc-exec"));     // -> custom-svc-exec
        await svc.CreateCustomCrewTemplateAsync(
            Template("svc-tpl", "custom-svc-exec", ["briefing-fidelity"]));

        // Caller passes an unprefixed target; service forces the custom- prefix.
        var final = await svc.RenameCustomExecutorProfileAsync("custom-svc-exec", "renamed-exec");

        Assert.Equal("custom-renamed-exec", final);
        await using var verify = NewContext();
        var tpl = await verify.CrewTemplates.AsNoTracking()
            .FirstAsync(t => t.Name == "custom-svc-tpl");
        Assert.Equal("custom-renamed-exec", tpl.ExecutorProfileName);
    }

    [Fact]
    public async Task Service_RenameCustomTemplate_RejectsSystemSource()
    {
        await using var ctx = NewContext();
        var svc = new CrewService(
            new ReviewerProfileRepository(ctx),
            new ExecutorProfileRepository(ctx),
            new AdvisorProfileRepository(ctx),
            new GroundingProviderProfileRepository(ctx),
            new CrewTemplateRepository(ctx));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RenameCustomCrewTemplateAsync(SystemCrew.KlassikTemplateName, "mine"));
    }

    [Fact]
    public async Task Service_RenameCustomExecutor_RejectsAlreadyUsedTargetName()
    {
        await using var ctx = NewContext();
        var svc = new CrewService(
            new ReviewerProfileRepository(ctx),
            new ExecutorProfileRepository(ctx),
            new AdvisorProfileRepository(ctx),
            new GroundingProviderProfileRepository(ctx),
            new CrewTemplateRepository(ctx));

        await svc.CreateCustomExecutorProfileAsync(Exec("dup-one"));   // custom-dup-one
        await svc.CreateCustomExecutorProfileAsync(Exec("dup-two"));   // custom-dup-two

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RenameCustomExecutorProfileAsync("custom-dup-one", "dup-two"));
        Assert.Contains("already in use", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
