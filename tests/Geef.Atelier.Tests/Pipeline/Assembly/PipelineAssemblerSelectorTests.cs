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
    private static RunEntity Run(string? seed = null, Guid? orderId = null) => new()
    {
        Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Running, BriefingText = "b",
        SeedDraftText = seed, OrderId = orderId,
    };

    public static TheoryData<string, Type> Cases => new()
    {
        { "direct", typeof(DirectPipelineAssembler) },
        { "direct-seeded", typeof(DirectPipelineAssembler) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Picks_the_assembler_by_priority(string kind, Type expected)
    {
        var (run, snapshot) = kind switch
        {
            "direct" => (Run(), DirectPipelineHarness.DirectSnapshot),
            "direct-seeded" => (Run(seed: "draft"), DirectPipelineHarness.DirectSnapshot),
            _ => (Run(), TestCrewSnapshots.Runnable),
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
        Assert.Single(services.GetServices<IPipelineAssembler>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("draft")]
    public void A_run_with_a_setup_no_assembler_accepts_is_refused_with_a_plain_sentence(string? seed)
    {
        var selector = new ServiceCollection().AddOptions().AddPipelineAssemblers().BuildServiceProvider().GetRequiredService<IPipelineAssemblerSelector>();
        var snapshot = DirectPipelineHarness.DirectSnapshot with { TemplateName = "unknown-setup" };

        var error = Assert.Throws<InvalidCrewSnapshotException>(() => selector.Select(Run(seed), snapshot));

        Assert.Equal(PipelineAssemblerSelector.UnknownSetupMessage, error.Message);
    }

    [Fact]
    public void A_direct_run_without_an_order_fails_with_a_plain_message()
    {
        var harness = new DirectPipelineHarness();
        var run = Run();

        var error = Assert.Throws<DirectPathException>(() => new DirectPipelineAssembler(Options.Create(new SourceOptions()), Options.Create(new DirectOptions())).Assemble(harness.Context(run)));

        Assert.Equal(DirectPathException.NoOrder, error.Message);
    }
}
