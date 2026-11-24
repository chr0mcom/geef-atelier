using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

/// <summary>
/// Q1 (Etappe 0 review): a vector-store profile without a scope or with an unknown scope must never return the
/// attachment of another user's run. Two users, real Postgres with pgvector, the real provider and repository.
/// </summary>
[Collection("Postgres")]
public sealed class VectorStoreAttachmentIsolationPostgresTests(PostgresFixture fixture)
{
    private const int Dimensions = 1536;
    private const string BobSecret = "BOB-PRIVATE-ATTACHMENT";
    private const string SharedText = "SHARED-KNOWLEDGE";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("everything")]
    [InlineData("RUN_LOCAL")]
    public async Task ProfileWithoutOrWithUnknownScopeNeverReturnsAnotherUsersAttachment(string? scope)
    {
        var (aliceRun, _) = await SeedTwoUsersAsync();
        var provider = BuildProvider();
        var settings = new Dictionary<string, string> { ["TopK"] = "50" };
        if (scope is not null)
            settings["Scope"] = scope;

        var result = await provider.EnrichAsync("anything", Profile(settings), aliceRun, CancellationToken.None);

        Assert.DoesNotContain(BobSecret, result.EnrichedContext);
        Assert.All(result.Citations, c => Assert.DoesNotContain(BobSecret, c.Snippet));
        Assert.Contains(SharedText, result.EnrichedContext);
    }

    [Fact]
    public async Task RunLocalScopeReturnsOnlyTheOwnAttachment()
    {
        var (aliceRun, _) = await SeedTwoUsersAsync();
        await SeedDocumentAsync(KnowledgeScope.RunLocal, aliceRun, "ALICE-OWN-ATTACHMENT");
        var provider = BuildProvider();

        var result = await provider.EnrichAsync("anything",
            Profile(new Dictionary<string, string> { ["TopK"] = "50", ["Scope"] = "run-local" }), aliceRun, CancellationToken.None);

        Assert.Contains("ALICE-OWN-ATTACHMENT", result.EnrichedContext);
        Assert.DoesNotContain(BobSecret, result.EnrichedContext);
        Assert.DoesNotContain(SharedText, result.EnrichedContext);
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

    private async Task<(Guid AliceRun, Guid BobRun)> SeedTwoUsersAsync()
    {
        Guid aliceRun, bobRun;
        await using (var db = fixture.NewContext())
        {
            var runs = new RunPersistenceService(db);
            aliceRun = await runs.CreateRunAsync("alice", "{}", createdByUser: "alice");
            bobRun = await runs.CreateRunAsync("bob", "{}", createdByUser: "bob");
        }

        await SeedDocumentAsync(KnowledgeScope.RunLocal, bobRun, BobSecret);
        await SeedDocumentAsync(KnowledgeScope.Global, null, SharedText);
        return (aliceRun, bobRun);
    }

    private async Task SeedDocumentAsync(KnowledgeScope scope, Guid? runId, string content)
    {
        await using var db = fixture.NewContext();
        var document = new KnowledgeDocument(Guid.NewGuid(), content, "", "doc.md", "text/markdown", content.Length, content,
            [], "stub", Dimensions, 1, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, scope, runId);
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
        services.AddScoped<IGroundingConsultationRepository, NullConsultations>();
        return new VectorStoreGroundingProvider(
            new FixedEmbeddingProvider(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
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
