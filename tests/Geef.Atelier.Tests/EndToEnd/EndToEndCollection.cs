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
}
