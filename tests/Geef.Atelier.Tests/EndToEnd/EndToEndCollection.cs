using Geef.Atelier.Tests.Persistence;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Shares one Testcontainers Postgres between all end-to-end tests (each host gets its own database) and
/// runs them alone, after the parallel collections, to stay clear of the inotify limit on the server.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EndToEndCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "EndToEnd";

    /// <summary>Trait name for tests that pin a defect of the 2026-11-24 analysis.</summary>
    public const string StageTrait = "Etappe0";

    /// <summary>Trait value: red on the unrepaired base, green once package A's grounding repair is merged.</summary>
    public const string AfterRepair = "NachReparatur";

    /// <summary>Trait value: red until tool invocations get their own sequence numbers (finding F2).</summary>
    public const string SequenceDefect = "Sequenzdefekt";
}
