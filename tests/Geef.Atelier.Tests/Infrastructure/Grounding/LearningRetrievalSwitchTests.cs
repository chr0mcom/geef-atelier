using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

/// <summary>Learning:Enabled switch for the learning-retrieval grounding provider, against real Postgres.</summary>
[Collection("Postgres")]
public sealed class LearningRetrievalSwitchTests(PostgresFixture fixture)
{
    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AtelierDbContext>(opt => opt.UseNpgsql(fixture.ConnectionString)
               .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddAtelierPersistence();
        services.AddAtelierApplication();
        services.AddSingleton<Geef.Atelier.Application.Crew.IModelCatalog, PassThroughModelCatalog>();
        services.AddScoped<IKnowledgeService, NoOpKnowledgeService>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static LearningRetrievalGroundingProvider MakeProvider(
        ServiceProvider sp, CountingEmbeddingProvider embedding, bool enabled) =>
        new(embedding,
            sp.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<LearningOptions>(new LearningOptions { Enabled = enabled }),
            NullLogger<LearningRetrievalGroundingProvider>.Instance);

    private static async Task<Guid> SubmitRunAsync(ServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRunService>()
            .SubmitRunAsync(new SubmitRunRequest("Learning switch briefing", "{}", CreatedByUser: "alice"));
    }

    private async Task<int> ConsultationCountAsync(Guid runId)
    {
        await using var ctx = fixture.NewContext();
        return await ctx.Database
            .SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM "GroundingConsultations" WHERE "RunId" = {runId}""")
            .SingleAsync();
    }

    [Fact]
    public async Task Disabled_ReturnsEmptyContext_WithoutEmbeddingOrConsultation()
    {
        await using var sp = BuildProvider();
        var runId = await SubmitRunAsync(sp);
        var embedding = new CountingEmbeddingProvider();

        var result = await MakeProvider(sp, embedding, enabled: false)
            .EnrichAsync("briefing", SystemCrew.LearningRetrieverDefaultProfile, runId, default);

        Assert.Equal(0, embedding.Calls);
        Assert.Equal(string.Empty, result.EnrichedContext);
        Assert.Empty(result.Citations);
        Assert.Null(result.ConsultationId);
        Assert.Equal(0, await ConsultationCountAsync(runId));
    }

    [Fact]
    public async Task Enabled_EmbedsBriefingAndRecordsConsultation()
    {
        await using var sp = BuildProvider();
        var runId = await SubmitRunAsync(sp);
        var embedding = new CountingEmbeddingProvider();

        var result = await MakeProvider(sp, embedding, enabled: true)
            .EnrichAsync("briefing", SystemCrew.LearningRetrieverDefaultProfile, runId, default);

        Assert.Equal(1, embedding.Calls);
        Assert.NotNull(result.ConsultationId);
        Assert.Equal(1, await ConsultationCountAsync(runId));
    }

    private sealed class CountingEmbeddingProvider : IEmbeddingProvider
    {
        public int Calls { get; private set; }
        public string ProviderName => "stub";
        public string ModelName => "stub-embedding";
        public int Dimensions => 1536;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new EmbeddingResult(Enumerable.Repeat(0.01f, 1536).ToArray(), 3, null));
        }

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<EmbeddingResult>>(texts.Select(_ => new EmbeddingResult(new float[1536], 3, null)).ToList());
    }
}
