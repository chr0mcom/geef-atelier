using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

/// <summary>
/// Q1 and B1 (Etappe 0 reviews): whatever the <c>Scope</c> setting of a vector-store profile says, the attachments of
/// another user's run are never returned. <c>both</c> returns the shared documents plus the attachments of the run and
/// its resumed-from parent; a missing setting and <c>global</c> return the shared documents only; an unknown value falls
/// back to the shared documents and records a run warning. Two users, real Postgres with pgvector, the real provider,
/// lineage and repository.
/// </summary>
[Collection("Postgres")]
public sealed class VectorStoreAttachmentIsolationPostgresTests(PostgresFixture fixture)
{
    private const int Dimensions = 1536;
    private const string BobSecret = "BOB-PRIVATE-ATTACHMENT";
    private const string SharedText = "SHARED-KNOWLEDGE";
    private const string AliceOwn = "ALICE-OWN-ATTACHMENT";
    private const string AliceParent = "ALICE-PARENT-ATTACHMENT";

    private readonly InMemoryRunWarnings _warnings = new();
    private readonly string _tag = Guid.NewGuid().ToString("N");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("global")]
    [InlineData(" Global ")]
    public async Task MissingOrGlobalScopeReturnsSharedDocumentsOnlyWithoutWarning(string? scope)
    {
        var (aliceRun, _) = await SeedTwoUsersAsync();

        var result = await EnrichAsync(aliceRun, scope);

        Assert.Contains(SharedText, result.EnrichedContext);
        AssertNoAttachments(result, AliceOwn, AliceParent, BobSecret);
        Assert.Empty(_warnings.All);
    }

    [Theory]
    [InlineData("run-local")]
    [InlineData("RUN-LOCAL")]
    public async Task RunLocalScopeReturnsOnlyTheOwnAndParentAttachments(string scope)
    {
        var (aliceRun, _) = await SeedTwoUsersAsync();

        var result = await EnrichAsync(aliceRun, scope);

        Assert.Contains(AliceOwn, result.EnrichedContext);
        Assert.Contains(AliceParent, result.EnrichedContext);
        AssertNoAttachments(result, BobSecret, SharedText);
        Assert.Empty(_warnings.All);
    }

    [Theory]
    [InlineData("both")]
    [InlineData(" Both ")]
    public async Task BothScopeReturnsSharedDocumentsPlusOwnAndParentAttachmentsButNeverAnotherUsers(string scope)
    {
        var (aliceRun, bobRun) = await SeedTwoUsersAsync();

        var result = await EnrichAsync(aliceRun, scope);

        Assert.Contains(SharedText, result.EnrichedContext);
        Assert.Contains(AliceOwn, result.EnrichedContext);
        Assert.Contains(AliceParent, result.EnrichedContext);
        AssertNoAttachments(result, BobSecret);
        Assert.Empty(_warnings.All);

        var bobResult = await EnrichAsync(bobRun, scope);

        Assert.Contains(BobSecret, bobResult.EnrichedContext);
        Assert.Contains(SharedText, bobResult.EnrichedContext);
        AssertNoAttachments(bobResult, AliceOwn, AliceParent);
    }

    [Fact]
    public async Task BothScopeWithoutAnyAttachmentStillReturnsSharedDocuments()
    {
        await SeedTwoUsersAsync();
        Guid carolRun;
        await using (var db = fixture.NewContext())
            carolRun = await new RunPersistenceService(db).CreateRunAsync("carol", "{}", createdByUser: "carol");

        var result = await EnrichAsync(carolRun, "both");

        Assert.Contains(SharedText, result.EnrichedContext);
        AssertNoAttachments(result, AliceOwn, AliceParent, BobSecret);
    }

    [Theory]
    [InlineData("everything")]
    [InlineData("RUN_LOCAL")]
    [InlineData("both,global")]
    public async Task UnknownScopeReturnsSharedDocumentsOnlyAndRecordsAPlainRunWarning(string scope)
    {
        var (aliceRun, _) = await SeedTwoUsersAsync();

        var result = await EnrichAsync(aliceRun, scope);

        Assert.Contains(SharedText, result.EnrichedContext);
        AssertNoAttachments(result, AliceOwn, AliceParent, BobSecret);
        var warning = Assert.Single(_warnings.All);
        Assert.Equal(aliceRun, warning.RunId);
        Assert.Equal(RunWarningSources.Grounding, warning.Source);
        Assert.Equal(RunWarningCodes.GroundingScopeUnknown, warning.Code);
        Assert.Equal(
            "The source \"Knowledge\" has an unknown search setting, so only shared documents were searched.",
            warning.Message);
        Assert.DoesNotContain(scope, warning.Message);
    }

    [Fact]
    public async Task RepositoryWithoutAnyFilterNeverReturnsRunLocalDocuments()
    {
        await SeedTwoUsersAsync();
        await using var db = fixture.NewContext();
        var repository = new VectorSearchRepository(db);

        var results = await repository.SearchAsync(Vector(), 500, tagFilter: null, scopeFilter: null, runIdFilter: null, CancellationToken.None);

        Assert.DoesNotContain(results, r => r.Chunk.Content.Contains(BobSecret, StringComparison.Ordinal));
        Assert.Contains(results, r => r.Chunk.Content.Contains(SharedText, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepositoryWithRunLocalScopeButNoRunIdReturnsNothing()
    {
        await SeedTwoUsersAsync();
        await using var db = fixture.NewContext();
        var repository = new VectorSearchRepository(db);

        var results = await repository.SearchAsync(Vector(), 500, tagFilter: null, KnowledgeScope.RunLocal, runIdFilter: null, CancellationToken.None);

        Assert.Empty(results);
    }

    private async Task<GroundingResult> EnrichAsync(Guid runId, string? scope)
    {
        var settings = new Dictionary<string, string> { ["TopK"] = "50", ["TagFilter"] = _tag };
        if (scope is not null)
            settings["Scope"] = scope;
        return await BuildProvider().EnrichAsync("anything", Profile(settings), runId, CancellationToken.None);
    }

    private static void AssertNoAttachments(GroundingResult result, params string[] texts)
    {
        foreach (var text in texts)
        {
            Assert.DoesNotContain(text, result.EnrichedContext);
            Assert.All(result.Citations, c => Assert.DoesNotContain(text, c.Snippet));
        }
    }

    private async Task<(Guid AliceRun, Guid BobRun)> SeedTwoUsersAsync()
    {
        Guid aliceParent, bobRun;
        await using (var db = fixture.NewContext())
        {
            var runs = new RunPersistenceService(db);
            aliceParent = await runs.CreateRunAsync("alice first", "{}", createdByUser: "alice");
            bobRun = await runs.CreateRunAsync("bob", "{}", createdByUser: "bob");
        }

        var aliceRun = Guid.NewGuid();
        await using (var db = fixture.NewContext())
        {
            db.Runs.Add(new RunEntity
            {
                Id = aliceRun, CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Pending, BriefingText = "alice resumed",
                ConfigJson = "{}", CreatedByUser = "alice", ParentRunId = aliceParent,
            });
            await db.SaveChangesAsync();
        }

        await SeedDocumentAsync(KnowledgeScope.RunLocal, bobRun, BobSecret);
        await SeedDocumentAsync(KnowledgeScope.Global, null, SharedText);
        await SeedDocumentAsync(KnowledgeScope.RunLocal, aliceRun, AliceOwn);
        await SeedDocumentAsync(KnowledgeScope.RunLocal, aliceParent, AliceParent);
        return (aliceRun, bobRun);
    }

    private async Task SeedDocumentAsync(KnowledgeScope scope, Guid? runId, string content)
    {
        await using var db = fixture.NewContext();
        var document = new KnowledgeDocument(Guid.NewGuid(), content, "", "doc.md", "text/markdown", content.Length, content,
            [_tag], "stub", Dimensions, 1, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, scope, runId);
        await new KnowledgeDocumentRepository(db).CreateAsync(document, CancellationToken.None);
        await new VectorSearchRepository(db).CreateChunkAsync(
            new KnowledgeDocumentChunk(Guid.NewGuid(), document.Id, 0, content, Vector(), 1, DateTimeOffset.UtcNow), CancellationToken.None);
    }

    private VectorStoreGroundingProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AtelierDbContext>(o => o
            .UseNpgsql(fixture.ConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddScoped<IVectorSearchRepository, VectorSearchRepository>();
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
        services.AddScoped<IRunRepository, RunRepository>();
        services.AddScoped<IRunAttachmentLineage, Geef.Atelier.Infrastructure.Knowledge.RunAttachmentLineage>();
        services.AddScoped<IGroundingConsultationRepository, NullConsultations>();
        return new VectorStoreGroundingProvider(
            new FixedEmbeddingProvider(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            _warnings,
            NullLogger<VectorStoreGroundingProvider>.Instance);
    }

    private static GroundingProviderProfile Profile(Dictionary<string, string> settings) =>
        new("studio-made-knowledge", "Knowledge", "made through the template studio", "vector-store", settings,
            MaxQueriesPerRun: null, IsSystem: false);

    private static float[] Vector() => Enumerable.Repeat(0.01f, Dimensions).ToArray();

    private sealed class FixedEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderName => "stub";
        public string ModelName => "stub";
        public int Dimensions => VectorStoreAttachmentIsolationPostgresTests.Dimensions;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct) =>
            Task.FromResult(new EmbeddingResult(Vector(), 1, null));

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class NullConsultations : IGroundingConsultationRepository
    {
        public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct) => Task.FromResult(consultation);
        public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct) => Task.FromResult<IReadOnlyList<GroundingConsultation>>([]);
        public Task UpdateRefinementOutcomeAsync(Guid consultationId, RefinementOutcome outcome, CancellationToken ct) => Task.CompletedTask;
    }
}
