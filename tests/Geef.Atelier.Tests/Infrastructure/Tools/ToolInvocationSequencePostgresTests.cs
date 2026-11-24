using System.Text.Json;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Repositories;
using Geef.Atelier.Infrastructure.Tools;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Tools;

/// <summary>
/// The tool audit against real Postgres: sequence numbers are assigned by the database, so several tool calls of
/// one run never collide on <c>IX_tool_invocations_RunId_Sequence</c>, and a failed audit write never discards the
/// tool result.
/// </summary>
[Collection("Postgres")]
public sealed class ToolInvocationSequencePostgresTests(PostgresFixture fixture)
{
    [Fact]
    public async Task TwoGroundingToolCallsInOneRunGetConsecutiveSequences()
    {
        var runId = await CreateRunAsync();
        var executor = BuildExecutor();

        var first = await executor.ExecuteAsync(StaticTool("one"), "{}", Context(runId, "tavily-refined"));
        var second = await executor.ExecuteAsync(StaticTool("two"), "{}", Context(runId, "run-attachments"));

        Assert.Equal(("one", "two"), (first.Output, second.Output));
        Assert.Equal([0, 1], await SequencesAsync(runId));
    }

    [Fact]
    public async Task ConcurrentToolCallsInOneRunAllPersistWithDistinctSequences()
    {
        var runId = await CreateRunAsync();
        var executor = BuildExecutor();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i =>
            executor.ExecuteAsync(StaticTool($"p{i}"), "{}", Context(runId, $"reviewer-{i}"))));

        Assert.All(results, r => Assert.Null(r.Error));
        Assert.Equal(Enumerable.Range(0, 6).ToList(), await SequencesAsync(runId));
    }

    [Fact]
    public async Task SixWritersThatAllCollideOnTheSameSequenceStillAllPersist()
    {
        const int writers = 6;
        var runId = await CreateRunAsync();
        var trigger = $"test_slow_insert_{Guid.NewGuid():N}";
        var create = $$"""
            CREATE FUNCTION {{trigger}}() RETURNS trigger LANGUAGE plpgsql AS $fn$ BEGIN PERFORM pg_sleep(0.3); RETURN NEW; END $fn$;
            CREATE TRIGGER {{trigger}} BEFORE INSERT ON tool_invocations FOR EACH ROW
                WHEN (NEW."RunId" = '{{runId}}') EXECUTE FUNCTION {{trigger}}();
            """;
        var drop = $"""DROP TRIGGER IF EXISTS {trigger} ON tool_invocations; DROP FUNCTION IF EXISTS {trigger}();""";
        await using (var db = fixture.NewContext())
            await db.Database.ExecuteSqlRawAsync(create);
        try
        {
            using var barrier = new Barrier(writers);
            var inserts = Enumerable.Range(0, writers).Select(i => Task.Run(async () =>
            {
                await using var db = fixture.NewContext();
                await db.Database.OpenConnectionAsync();
                barrier.SignalAndWait(TimeSpan.FromSeconds(30));
                await new ToolInvocationRepository(db).AddAsync(Invocation(runId, $"writer-{i}"));
            })).ToList();
            await Task.WhenAll(inserts).WaitAsync(TimeSpan.FromSeconds(60));
        }
        finally
        {
            await using var db = fixture.NewContext();
            await db.Database.ExecuteSqlRawAsync(drop);
        }

        Assert.Equal(Enumerable.Range(0, writers).ToList(), await SequencesAsync(runId));
    }

    [Fact]
    public async Task ExistingLegacyRowWithSequenceZeroStaysValidAndTheNextCallContinues()
    {
        var runId = await CreateRunAsync();
        await using (var db = fixture.NewContext())
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO tool_invocations ("Id","RunId","IterationNumber","ActorType","ActorName","ToolName","ToolType",
                    "InputJson","OutputExcerpt","CostEur","DurationMs","Sequence","Outcome","CreatedAt")
                VALUES ({Guid.NewGuid()}, {runId}, 0, 'grounding', 'legacy', 'web-search', 'web-search', 'legacy-input', NULL, NULL, 1, 0, 1, now())
                """);
        }

        await BuildExecutor().ExecuteAsync(StaticTool("new"), "{}", Context(runId, "crew-design-rules"));

        Assert.Equal([0, 1], await SequencesAsync(runId));
    }

    [Fact]
    public async Task FailedAuditWriteKeepsTheResultAndDoesNotBlockLaterCalls()
    {
        var runId = await CreateRunAsync();
        var executor = BuildExecutor();

        var broken = await executor.ExecuteAsync(StaticTool("still delivered"), "{}", Context(runId, new string('x', 150)));
        var next = await executor.ExecuteAsync(StaticTool("next"), "{}", Context(runId, "after-failure"));

        Assert.Equal("still delivered", broken.Output);
        Assert.Null(broken.Error);
        Assert.Equal("next", next.Output);
        Assert.Equal([0], await SequencesAsync(runId));
    }

    private ToolExecutor BuildExecutor()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AtelierDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddScoped<IToolInvocationRepository, ToolInvocationRepository>();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new ToolExecutor(scopeFactory, new NoOpHttpClientFactory(), new NullMcpServerConfigRepository(),
            new NullMcpClientFactory(), NullLogger<ToolExecutor>.Instance);
    }

    private async Task<Guid> CreateRunAsync()
    {
        await using var db = fixture.NewContext();
        return await new RunPersistenceService(db).CreateRunAsync("sequence test", "{}");
    }

    private async Task<List<int>> SequencesAsync(Guid runId)
    {
        await using var db = fixture.NewContext();
        return await db.Database
            .SqlQuery<int>($"""SELECT "Sequence" AS "Value" FROM tool_invocations WHERE "RunId" = {runId} ORDER BY "Sequence" """)
            .ToListAsync();
    }

    private static ToolInvocation Invocation(Guid runId, string actorName) => new()
    {
        Id = Guid.NewGuid(),
        RunId = runId,
        ActorType = "grounding",
        ActorName = actorName,
        ToolName = "static-context",
        ToolType = "static-context",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ToolInvocationContext Context(Guid runId, string actorName) =>
        new(RunId: runId, IterationNumber: 0, ActorType: actorName.Length > 100 ? actorName : "grounding", ActorName: actorName[..Math.Min(actorName.Length, 100)], Sequence: 0);

    private static ToolDefinition StaticTool(string content) =>
        new(
            Name: "static-context",
            DisplayName: "Static Context",
            Description: "test",
            ToolType: ToolType.StaticContext,
            Settings: new Dictionary<string, string> { [ToolDefinitionSettingsKeys.StaticContent] = content },
            SecretRef: null,
            LlmSchema: JsonDocument.Parse("{}").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: true);
}
