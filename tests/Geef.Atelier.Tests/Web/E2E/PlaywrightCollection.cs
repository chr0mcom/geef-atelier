namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>xUnit collection that shares a single <see cref="PlaywrightFixture"/> and <see cref="Geef.Atelier.Tests.Persistence.PostgresFixture"/> across all Playwright E2E tests.</summary>
[CollectionDefinition("Playwright")]
public sealed class PlaywrightCollection
    : ICollectionFixture<PlaywrightFixture>,
      ICollectionFixture<Geef.Atelier.Tests.Persistence.PostgresFixture>;
