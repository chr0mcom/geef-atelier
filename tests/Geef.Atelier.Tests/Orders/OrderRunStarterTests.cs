using System.Reflection;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary><see cref="OrderRunStarter"/> tells the composer that the assumptions were shown only when the run starts from the card.</summary>
public sealed class OrderRunStarterTests
{
    [Theory]
    [InlineData(OrderState.NeedsInput, IntakeOutcome.Answered, true)]
    [InlineData(OrderState.NeedsInput, IntakeOutcome.AutoStarted, true)]
    [InlineData(OrderState.Analyzing, IntakeOutcome.Clarified, false)]
    [InlineData(OrderState.Analyzing, IntakeOutcome.Failed, false)]
    public async Task The_composer_learns_whether_the_card_was_shown(OrderState expected, IntakeOutcome outcome, bool shown)
    {
        var composer = new RecordingComposer();
        var starter = Starter(composer);
        var order = Order("user-a", "Schreib eine Beschwerde.", expected);

        var runId = await starter.TryStartAsync(new RunStartRequest(order, expected, IntakeBriefComposerTests.Record(), [], outcome, RecordIsNew: true), CancellationToken.None);

        Assert.NotNull(runId);
        Assert.Equal(shown, Assert.Single(composer.Inputs).AssumptionsShown);
    }

    [Fact]
    public async Task A_run_started_without_the_card_never_calls_the_assumptions_shown_to_the_person()
    {
        var composer = new IntakeBriefComposer(Options.Create(new IntakeOptions()));
        var starts = new List<OrderStart>();
        var starter = Starter(composer, starts);
        var order = Order("user-a", "Schreib eine Beschwerde.", OrderState.Analyzing);

        await starter.TryStartAsync(new RunStartRequest(order, OrderState.Analyzing, IntakeBriefComposerTests.Record(), [], IntakeOutcome.Clarified, true), CancellationToken.None);

        var briefing = Assert.Single(starts).Run.BriefingText;
        Assert.DoesNotContain("shown to the person", briefing, StringComparison.Ordinal);
        Assert.Contains(IntakeBriefComposer.AssumptionsNotShownHeading, briefing, StringComparison.Ordinal);
    }

    private static OrderRunStarter Starter(IOrderBriefComposer composer, List<OrderStart>? starts = null) => new(
        ScriptedProxy<IOrderRepository>.Create((method, args) => method.Name switch
        {
            nameof(IOrderRepository.TryStartWithRunAsync) => Record(starts, (OrderStart)args![0]!),
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
        ScriptedProxy<ICrewService>.Create((method, _) => method.Name switch
        {
            nameof(ICrewService.ResolveCodeTemplateSnapshotAsync) =>
                Task.FromResult(new ResolvedCrewSnapshot(Snapshot, [])),
            _ => throw new InvalidOperationException(method.Name),
        }),
        composer,
        new InMemoryRunWarnings(),
        ScriptedProxy<IOrderNotifier>.Create((_, _) => Task.CompletedTask),
        NullLogger<OrderRunStarter>.Instance,
        TimeProvider.System);

    private static Task<bool> Record(List<OrderStart>? starts, OrderStart start)
    {
        starts?.Add(start);
        return Task.FromResult(true);
    }

    private static readonly CrewSnapshot Snapshot = new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.DirectTemplateName,
        Executor: SystemCrew.DirectWriterProfile,
        Reviewers: [SystemCrew.DeterministicChecksProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null);

    private sealed class RecordingComposer : IOrderBriefComposer
    {
        public List<OrderBriefInput> Inputs { get; } = [];

        public IntakeBrief Compose(OrderBriefInput input)
        {
            Inputs.Add(input);
            return new IntakeBrief(input.Order.RequestText, [IntakeBriefComposer.RequestSection]);
        }
    }
}

/// <summary>Stand-in whose calls are answered by a handler; unhandled calls fail the test.</summary>
public class ScriptedProxy<T> : DispatchProxy where T : class
{
    private Func<MethodInfo, object?[]?, object?> _handler = (method, _) => throw new InvalidOperationException(method.Name);

    /// <summary>Creates a stand-in for <typeparamref name="T"/> that answers with <paramref name="handler"/>.</summary>
    public static T Create(Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = Create<T, ScriptedProxy<T>>();
        ((ScriptedProxy<T>)(object)proxy)._handler = handler;
        return proxy;
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _handler(targetMethod!, args);
}
