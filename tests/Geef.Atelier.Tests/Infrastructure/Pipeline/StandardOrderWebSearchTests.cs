using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Persistence;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Infrastructure.Pipeline;

/// <summary>D-079: which runs drop the outside search sources, against the real database.</summary>
[Collection("Postgres")]
public sealed class StandardOrderWebSearchTests(PostgresFixture postgres)
{
    private static readonly GroundingProviderProfile RestApi = Profile("own-api", "rest-api");
    private static readonly GroundingProviderProfile RetiredNews = Profile("tavily-news", "news-search") with { IsSystem = true };
    private static readonly GroundingProviderProfile RetiredAcademic = Profile("academic-default", "academic-search") with { IsSystem = true };
    private static readonly GroundingProviderProfile UrlFetch = Profile("own-pages", GroundingProviderTypes.UrlFetch);
    private static readonly GroundingProviderProfile LocalTypeThroughTool = Profile("own-tool", GroundingProviderTypes.VectorStore) with { ToolName = "web-search" };
    private static readonly GroundingProviderProfile SystemAttachmentsWithOldToolName = SystemCrew.RunAttachmentsProfile with { ToolName = "vector-search" };

    private static readonly IReadOnlyList<GroundingProviderProfile> Mixed =
    [
        SystemCrew.OrderSourcesProfile,
        SystemCrew.TavilyRefinedProfile,
        SystemCrew.TavilyBasicProfile,
        RetiredNews,
        RetiredAcademic,
        RestApi,
        UrlFetch,
        LocalTypeThroughTool,
        SystemAttachmentsWithOldToolName,
        SystemCrew.KnowledgeBaseDefaultProfile,
        SystemCrew.LearningRetrieverDefaultProfile,
    ];

    [Fact]
    public void WithoutOutsideSearch_keeps_only_the_sources_that_stay_inside_in_their_order()
    {
        var local = Snapshot(SystemCrew.KlassikTemplateName, Mixed).WithoutOutsideSearch();

        Assert.Equal(
            [SystemCrew.OrderSourcesProfile.Name, SystemAttachmentsWithOldToolName.Name, SystemCrew.KnowledgeBaseDefaultProfile.Name, SystemCrew.LearningRetrieverDefaultProfile.Name],
            local.GroundingProviders!.Select(p => p.Name));
    }

    [Fact]
    public void WithoutOutsideSearch_returns_the_same_snapshot_when_nothing_searches_outside()
    {
        var snapshot = Snapshot(SystemCrew.KlassikTemplateName, [SystemCrew.OrderSourcesProfile, SystemCrew.LearningRetrieverDefaultProfile]);

        Assert.Same(snapshot, snapshot.WithoutOutsideSearch());
    }

    [Fact]
    public async Task A_run_of_a_default_order_loses_every_outside_source()
    {
        var order = await StoreOrderAsync(crewTemplateName: null);
        var run = await StoreRunAsync(Run("u", order.Id, 1));

        var result = await ForRunAsync(run, Snapshot(SystemCrew.KlassikTemplateName, Mixed));

        Assert.All(result.GroundingProviders!, p => Assert.True(p.StaysLocal(), p.Name));
        Assert.DoesNotContain(result.GroundingProviders!, p => p.Name == SystemCrew.TavilyRefinedProfile.Name);
    }

    [Fact]
    public async Task A_resumed_run_that_is_not_linked_yet_follows_the_order_of_the_run_it_continues()
    {
        var order = await StoreOrderAsync(crewTemplateName: null);
        var parent = await StoreRunAsync(Run("u", order.Id, 1, status: RunStatus.Failed));
        var resumed = await StoreRunAsync(Run("u", parentRunId: parent.Id));

        var result = await ForRunAsync(resumed, Snapshot(SystemCrew.KlassikTemplateName, Mixed));

        Assert.All(result.GroundingProviders!, p => Assert.True(p.StaysLocal(), p.Name));
    }

    [Fact]
    public async Task A_template_named_by_the_order_keeps_its_sources()
    {
        var order = await StoreOrderAsync(crewTemplateName: SystemCrew.KlassikTemplateName);
        var run = await StoreRunAsync(Run("u", order.Id, 1));
        var snapshot = Snapshot(SystemCrew.KlassikTemplateName, Mixed);

        Assert.Same(snapshot, await ForRunAsync(run, snapshot));
    }

    [Fact]
    public async Task A_crew_composed_for_the_order_keeps_its_sources_even_when_it_is_the_default_one()
    {
        var order = await StoreOrderAsync(crewTemplateName: null);
        var composition = await StoreRunAsync(Run("u", order.Id, 1, kind: RunKind.CrewComposition, status: RunStatus.Completed));
        var task = await StoreRunAsync(Run("u", order.Id, 1, parentCompositionRunId: composition.Id));
        var snapshot = Snapshot(SystemCrew.KlassikTemplateName, Mixed);

        Assert.Same(snapshot, await ForRunAsync(task, snapshot));
    }

    [Fact]
    public async Task Another_template_an_own_crew_and_a_run_without_order_keep_their_sources()
    {
        var order = await StoreOrderAsync(crewTemplateName: null);
        var run = await StoreRunAsync(Run("u", order.Id, 1));
        var lonely = await StoreRunAsync(Run("u"));
        var legal = Snapshot("juristisch", Mixed);
        var own = Snapshot(null, Mixed);
        var classic = Snapshot(SystemCrew.KlassikTemplateName, Mixed);

        Assert.Same(legal, await ForRunAsync(run, legal));
        Assert.Same(own, await ForRunAsync(run, own));
        Assert.Same(classic, await ForRunAsync(lonely, classic));
    }

    private async Task<CrewSnapshot> ForRunAsync(RunEntity run, CrewSnapshot snapshot)
    {
        await using var db = postgres.NewContext();
        return await db.ForRunAsync(run, snapshot, CancellationToken.None);
    }

    private async Task<OrderEntity> StoreOrderAsync(string? crewTemplateName)
    {
        var order = Order(NewUser());
        order = new OrderEntity
        {
            Id = order.Id, CreatedByUser = order.CreatedByUser, RequestText = order.RequestText, Mode = OrderMode.Careful,
            State = order.State, Entry = order.Entry, CrewTemplateName = crewTemplateName, CreatedAt = order.CreatedAt, UpdatedAt = order.UpdatedAt,
        };
        await using var db = postgres.NewContext();
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    private async Task<RunEntity> StoreRunAsync(RunEntity run)
    {
        await using var db = postgres.NewContext();
        db.Runs.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    private static CrewSnapshot Snapshot(string? templateName, IReadOnlyList<GroundingProviderProfile> providers) => new(
        CrewSnapshot.CurrentSchemaVersion, templateName, SystemCrew.DefaultExecutorProfile, [], EvaluationStrategy.Parallel, null, [], providers);

    private static GroundingProviderProfile Profile(string name, string type) =>
        new(name, name, "Synthetic test source.", type, new Dictionary<string, string>(), 1, IsSystem: false);
}
