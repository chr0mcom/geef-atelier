using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Profiles;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// <c>Orders:CarefulOutline</c> (D-087): which template an order gets. Only an "Extra careful" order without a chosen team and without a
/// custom crew of an owner for whom the switch is on gets the outline template; everything else resolves exactly as before. The switch
/// ships off, reads per user name from <c>CarefulOutlineOverrides</c>, and the progress display shows the direct steps for such a run.
/// </summary>
public sealed class CarefulOutlineRoutingTests
{
    private const string Owner = "meter";

    [Fact]
    public void The_switch_ships_off_without_overrides_and_reads_overrides_per_user_name()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build()
            .GetSection(OrderOptions.SectionName).Get<OrderOptions>()!;
        Assert.Equal(CarefulOutlineMode.Off, shipped.CarefulOutline);
        Assert.Empty(shipped.CarefulOutlineOverrides);
        Assert.Equal(CarefulOutlineMode.Off, new OrderOptions().CarefulOutline);

        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [$"Orders:CarefulOutlineOverrides:{Owner}"] = "On" })
            .Build()
            .GetSection(OrderOptions.SectionName).Get<OrderOptions>()!;
        Assert.Equal(CarefulOutlineMode.On, configured.CarefulOutlineFor(Owner));
        Assert.Equal(CarefulOutlineMode.On, configured.CarefulOutlineFor("METER"));
        Assert.Equal(CarefulOutlineMode.Off, configured.CarefulOutlineFor("someone-else"));
        Assert.Equal(CarefulOutlineMode.Off, configured.CarefulOutlineFor(null));
        Assert.Equal(CarefulOutlineMode.Off, new OrderOptions { CarefulOutline = CarefulOutlineMode.On, CarefulOutlineOverrides = { [Owner] = CarefulOutlineMode.Off } }.CarefulOutlineFor(Owner));
    }

    [Theory]
    [InlineData(OrderMode.Careful, null, true, SystemCrew.DirectOutlineTemplateName)]
    [InlineData(OrderMode.Careful, null, false, SystemCrew.KlassikTemplateName)]
    [InlineData(OrderMode.Careful, "juristisch", true, "juristisch")]
    [InlineData(OrderMode.Direct, null, true, SystemCrew.DirectTemplateName)]
    [InlineData(OrderMode.Direct, null, false, SystemCrew.DirectTemplateName)]
    public async Task Only_a_careful_order_without_a_team_takes_the_outline(OrderMode mode, string? template, bool outline, string expected)
    {
        var crew = new RecordingCrew();

        var resolved = await crew.Service.ResolveOrderSnapshotAsync(mode, template, null, hasSources: false, CancellationToken.None, outline);

        Assert.Equal(expected, resolved.Snapshot.TemplateName);
    }

    [Fact]
    public async Task A_custom_crew_never_takes_the_outline()
    {
        var crew = new RecordingCrew();
        var spec = new CrewSpec(SystemCrew.DefaultExecutorProfile.Name, [SystemCrew.BriefingFidelityProfile.Name], EvaluationStrategy.Parallel, null);

        var resolved = await crew.Service.ResolveOrderSnapshotAsync(OrderMode.Careful, null, spec, hasSources: false, CancellationToken.None, carefulOutline: true);

        Assert.Equal("custom", resolved.Snapshot.TemplateName);
        Assert.DoesNotContain(SystemCrew.DirectOutlineTemplateName, crew.Requested);
    }

    [Fact]
    public async Task The_outline_template_is_the_direct_template_under_its_own_name_and_keeps_the_files_out_of_the_crew_grounding()
    {
        var crew = new RecordingCrew();

        var resolved = await crew.Service.ResolveOrderSnapshotAsync(OrderMode.Careful, null, null, hasSources: true, CancellationToken.None, carefulOutline: true);

        Assert.Equal(SystemCrew.DirectOutlineTemplateName, resolved.Snapshot.TemplateName);
        Assert.Null(resolved.Snapshot.GroundingProviders);
        Assert.Equal(SystemCrew.DirectTemplate with
        {
            Name = SystemCrew.DirectOutlineTemplateName,
            DisplayName = SystemCrew.DirectOutlineTemplate.DisplayName,
            Description = SystemCrew.DirectOutlineTemplate.Description,
            ReviewerProfileNames = SystemCrew.DirectOutlineTemplate.ReviewerProfileNames,
        }, SystemCrew.DirectOutlineTemplate);
        Assert.Equal([.. SystemCrew.DirectTemplate.ReviewerProfileNames, SystemCrew.RequirementsCheckProfile.Name], SystemCrew.DirectOutlineTemplate.ReviewerProfileNames);
        Assert.True(SystemCrew.IsDirectPathTemplate(SystemCrew.DirectOutlineTemplateName));
        Assert.True(SystemCrew.IsDirectPathTemplate(SystemCrew.DirectTemplateName));
        Assert.False(SystemCrew.IsDirectPathTemplate(SystemCrew.KlassikTemplateName));
        Assert.False(SystemCrew.IsDirectPathTemplate(null));
        Assert.Same(SystemCrew.DirectOutlineTemplate, SystemCrew.CodeOnlyTemplates[SystemCrew.DirectOutlineTemplateName]);
        Assert.DoesNotContain(SystemCrew.DirectOutlineTemplateName, SystemCrew.CrewTemplates.Keys);
    }

    [Theory]
    [InlineData(Owner, SystemCrew.DirectOutlineTemplateName)]
    [InlineData("someone-else", SystemCrew.KlassikTemplateName)]
    public async Task A_careful_order_started_after_the_intake_takes_the_switch_of_its_owner(string owner, string expected)
    {
        var starts = new List<OrderStart>();
        var options = new OrderOptions { CarefulOutlineOverrides = { [Owner] = CarefulOutlineMode.On } };
        var starter = Starter(new RecordingCrew(), starts, Options.Create(options));
        var order = Order(owner, "Schreib einen langen Bericht.", OrderState.Analyzing) with { Mode = OrderMode.Careful };

        await starter.TryStartAsync(new RunStartRequest(order, OrderState.Analyzing, IntakeBriefComposerTests.Record(), [], IntakeOutcome.Clarified, true), CancellationToken.None);

        Assert.Equal(expected, Assert.Single(starts).Run.CrewTemplateName);
    }

    [Fact]
    public async Task Without_options_the_starter_resolves_as_before()
    {
        var starts = new List<OrderStart>();
        var starter = Starter(new RecordingCrew(), starts, null);
        var order = Order(Owner, "Schreib einen langen Bericht.", OrderState.Analyzing) with { Mode = OrderMode.Careful };

        await starter.TryStartAsync(new RunStartRequest(order, OrderState.Analyzing, IntakeBriefComposerTests.Record(), [], IntakeOutcome.Clarified, true), CancellationToken.None);

        Assert.Equal(SystemCrew.KlassikTemplateName, Assert.Single(starts).Run.CrewTemplateName);
    }

    [Theory]
    [InlineData(SystemCrew.DirectOutlineTemplateName, OrderMode.Careful, OrderMode.Direct)]
    [InlineData(SystemCrew.KlassikTemplateName, OrderMode.Careful, OrderMode.Careful)]
    [InlineData(SystemCrew.DirectTemplateName, OrderMode.Careful, OrderMode.Careful)]
    [InlineData(SystemCrew.DirectTemplateName, OrderMode.Direct, OrderMode.Direct)]
    [InlineData(null, OrderMode.Careful, OrderMode.Careful)]
    public void The_progress_shows_the_direct_steps_only_for_a_run_with_the_outline(string? template, OrderMode mode, OrderMode expected)
    {
        var order = Order(Owner, "Text", OrderState.Ready) with { Mode = mode };
        var run = template is null ? null : new RunEntity { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Completed, BriefingText = "x", ConfigJson = "{}", CrewTemplateName = template };

        Assert.Equal(expected, order.ProgressMode(run));
    }

    private static OrderRunStarter Starter(RecordingCrew crew, List<OrderStart> starts, IOptions<OrderOptions>? options) => new(
        ScriptedProxy<IOrderRepository>.Create((method, args) => method.Name switch
        {
            nameof(IOrderRepository.TryStartWithRunAsync) => Started(starts, (OrderStart)args![0]!),
            _ => throw new InvalidOperationException(method.Name),
        }),
        ScriptedProxy<ISourceRepository>.Create((method, _) => method.Name switch
        {
            nameof(ISourceRepository.ListForOrderAsync) => Task.FromResult<IReadOnlyList<OrderSourceItem>>([]),
            _ => throw new InvalidOperationException(method.Name),
        }),
        ScriptedProxy<IUserProfileRepository>.Create((method, _) => method.Name switch
        {
            nameof(IUserProfileRepository.GetAsync) => Task.FromResult<UserProfileEntity?>(null),
            _ => throw new InvalidOperationException(method.Name),
        }),
        crew.Service,
        new IntakeBriefComposer(Options.Create(new IntakeOptions())),
        new InMemoryRunWarnings(),
        ScriptedProxy<IOrderNotifier>.Create((_, _) => Task.CompletedTask),
        NullLogger<OrderRunStarter>.Instance,
        TimeProvider.System,
        options);

    private static Task<bool> Started(List<OrderStart> starts, OrderStart start)
    {
        starts.Add(start);
        return Task.FromResult(true);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Crew service stand-in that answers every resolution with a snapshot named after the requested template.</summary>
    private sealed class RecordingCrew
    {
        public List<string> Requested { get; } = [];

        public ICrewService Service => ScriptedProxy<ICrewService>.Create((method, args) =>
        {
            switch (method.Name)
            {
                case nameof(ICrewService.ResolveCodeTemplateSnapshotAsync):
                    var template = (CrewTemplate)args![0]!;
                    Requested.Add(template.Name);
                    return Task.FromResult(new ResolvedCrewSnapshot(Snapshot(template.Name), []));
                case nameof(ICrewService.ResolveSnapshotWithWarningsAsync):
                    var name = (string?)args![0];
                    var custom = args[1] is not null;
                    Requested.Add(custom ? "custom" : name ?? "(none)");
                    return Task.FromResult(new ResolvedCrewSnapshot(Snapshot(custom ? "custom" : name!), []));
                default:
                    throw new InvalidOperationException(method.Name);
            }
        });

        private static CrewSnapshot Snapshot(string name) => new(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: name,
            Executor: SystemCrew.DirectWriterProfile,
            Reviewers: [SystemCrew.DeterministicChecksProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: [],
            GroundingProviders: null);
    }
}
