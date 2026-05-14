using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Infrastructure.Persistence.TemplateStudio;

namespace Geef.Atelier.Tests.Persistence;

[Collection("Postgres")]
public sealed class TemplateStudioAnalysisRepositoryTests(PostgresFixture fixture)
{
    private TemplateStudioAnalysisRepository Repo() => new(fixture.NewContext());

    private static TemplateStudioAnalysis BuildAnalysis(
        StudioRecommendation recommendation = StudioRecommendation.UseExistingTemplate,
        DateTimeOffset? createdAt = null) => new(
        Id: Guid.NewGuid(),
        TaskDescription: "Write a test task description.",
        MatchedExistingTemplates: [new TemplateMatch("klassik", 0.9, "Good fit.")],
        Recommendation: recommendation,
        ProposedTemplate: null,
        ProposedNewProfiles: [],
        ReasoningSummary: "The existing template fits well.",
        InputTokens: 100,
        OutputTokens: 50,
        CostEur: 0.005m,
        CreatedAt: createdAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public async Task CreateAsync_ThenGetByIdAsync_ReturnsCorrectAnalysis()
    {
        var repo = Repo();
        var analysis = BuildAnalysis();

        await repo.CreateAsync(analysis, CancellationToken.None);
        var retrieved = await repo.GetByIdAsync(analysis.Id, CancellationToken.None);

        Assert.NotNull(retrieved);
        Assert.Equal(analysis.Id, retrieved!.Id);
        Assert.Equal(analysis.TaskDescription, retrieved.TaskDescription);
        Assert.Equal(analysis.Recommendation, retrieved.Recommendation);
        Assert.Equal(analysis.ReasoningSummary, retrieved.ReasoningSummary);
        Assert.Equal(analysis.InputTokens, retrieved.InputTokens);
        Assert.Equal(analysis.OutputTokens, retrieved.OutputTokens);
        Assert.Single(retrieved.MatchedExistingTemplates);
        Assert.Equal("klassik", retrieved.MatchedExistingTemplates[0].TemplateName);
        Assert.Equal(0.9, retrieved.MatchedExistingTemplates[0].Confidence);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var repo = Repo();
        var result = await repo.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task MarkMaterializedAsync_UpdatesRecord()
    {
        var repo = Repo();
        var analysis = BuildAnalysis();
        await repo.CreateAsync(analysis, CancellationToken.None);

        await repo.MarkMaterializedAsync(analysis.Id, "custom-legal-template", CancellationToken.None);

        // Verify the row was updated by checking via ADO.NET
        await using var conn = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            @"SELECT ""MaterializedTemplateName"" FROM ""TemplateStudioAnalyses"" WHERE ""Id"" = @id", conn);
        cmd.Parameters.AddWithValue("@id", analysis.Id);
        var value = await cmd.ExecuteScalarAsync();

        Assert.Equal("custom-legal-template", value as string);
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsInDescendingCreatedAtOrder()
    {
        var repo = Repo();

        var older = BuildAnalysis(createdAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = BuildAnalysis(createdAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var newest = BuildAnalysis(createdAt: DateTimeOffset.UtcNow);

        // Insert in non-chronological order to verify ordering
        await repo.CreateAsync(newer, CancellationToken.None);
        await repo.CreateAsync(older, CancellationToken.None);
        await repo.CreateAsync(newest, CancellationToken.None);

        var list = await repo.ListRecentAsync(limit: 10, CancellationToken.None);

        // The first items should be the most recent
        var newestIndex = list.ToList().FindIndex(a => a.Id == newest.Id);
        var newerIndex  = list.ToList().FindIndex(a => a.Id == newer.Id);
        var olderIndex  = list.ToList().FindIndex(a => a.Id == older.Id);

        Assert.True(newestIndex < newerIndex, "newest should appear before newer");
        Assert.True(newerIndex < olderIndex, "newer should appear before older");
    }
}
