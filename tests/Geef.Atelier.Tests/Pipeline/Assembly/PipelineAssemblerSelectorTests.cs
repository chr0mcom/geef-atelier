using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Pipeline.Direct;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline.Assembly;

public sealed class PipelineAssemblerSelectorTests
{
    private static RunEntity Run(RunKind kind = RunKind.Standard, string? seed = null, Guid? orderId = null) => new()
    {
        Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Running, BriefingText = "b", ConfigJson = "{}",
        Kind = kind, SeedDraftText = seed, OrderId = orderId,
    };

    private static readonly CrewSnapshot Composer = TestCrewSnapshots.Klassik with { TemplateName = SystemCrew.CrewComposerTemplateName };

    public static TheoryData<string, Type> Cases => new()
    {
        { "classic", typeof(StandardPipelineAssembler) },
        { "resume", typeof(SeedDraftPipelineAssembler) },
        { "composition-kind", typeof(CompositionPipelineAssembler) },
        { "composer-template", typeof(CompositionPipelineAssembler) },
        { "direct", typeof(DirectPipelineAssembler) },
        { "direct-seeded", typeof(DirectPipelineAssembler) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Picks_the_assembler_by_priority(string kind, Type expected)
    {
        var (run, snapshot) = kind switch
        {
            "resume" => (Run(seed: "draft"), TestCrewSnapshots.Klassik),
            "composition-kind" => (Run(RunKind.CrewComposition), TestCrewSnapshots.Klassik),
            "composer-template" => (Run(seed: "draft"), Composer),
            "direct" => (Run(), DirectPipelineHarness.DirectSnapshot),
            "direct-seeded" => (Run(seed: "draft"), DirectPipelineHarness.DirectSnapshot),
            _ => (Run(), TestCrewSnapshots.Klassik),
        };

        var selector = new ServiceCollection().AddOptions().AddPipelineAssemblers().BuildServiceProvider().GetRequiredService<IPipelineAssemblerSelector>();

        Assert.IsType(expected, selector.Select(run, snapshot));
    }

    [Fact]
    public void The_di_registration_builds_the_same_selector()
    {
        var services = new ServiceCollection().AddOptions().AddPipelineAssemblers().BuildServiceProvider();
        var selector = services.GetRequiredService<IPipelineAssemblerSelector>();

        Assert.IsType<DirectPipelineAssembler>(selector.Select(Run(), DirectPipelineHarness.DirectSnapshot));
        Assert.IsType<StandardPipelineAssembler>(selector.Select(Run(), TestCrewSnapshots.Klassik));
        Assert.Equal(4, services.GetServices<IPipelineAssembler>().Count());
    }

    [Fact]
    public void A_direct_run_without_an_order_fails_with_a_plain_message()
    {
        var harness = new DirectPipelineHarness();
        var run = Run();

        var error = Assert.Throws<DirectPathException>(() => new DirectPipelineAssembler(Options.Create(new SourceOptions())).Assemble(harness.Context(run)));

        Assert.Equal(DirectPathException.NoOrder, error.Message);
    }
}
