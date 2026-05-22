using Geef.Atelier.Application.SiteSettings;
using Geef.Atelier.Core.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using DomainSiteSettings = Geef.Atelier.Core.Domain.SiteSettings;
using SiteSettingsService = Geef.Atelier.Infrastructure.SiteSettings.SiteSettingsService;

namespace Geef.Atelier.Tests.Application.SiteSettings;

public sealed class SiteSettingsServiceTests
{
    private static DomainSiteSettings DefaultSettings() => new()
    {
        Id = Guid.NewGuid(),
        OperatorName = "Test GmbH",
        AddressStreet = "Teststraße 1",
        AddressZip = "12345",
        AddressCity = "Teststadt",
        AddressCountry = "Deutschland",
        ContactEmail = "test@example.com",
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static IMemoryCache CreateCache() =>
        new MemoryCache(Options.Create(new MemoryCacheOptions()));

    private sealed class StubRepository(DomainSiteSettings settings) : ISiteSettingsRepository
    {
        public int GetCallCount { get; private set; }
        public DomainSiteSettings? LastUpdated { get; private set; }

        public Task<DomainSiteSettings> GetAsync(CancellationToken ct = default)
        {
            GetCallCount++;
            return Task.FromResult(settings);
        }

        public Task UpdateAsync(DomainSiteSettings s, CancellationToken ct = default)
        {
            LastUpdated = s;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task GetAsync_CacheMiss_LoadsFromRepository()
    {
        var repo = new StubRepository(DefaultSettings());
        var svc = new SiteSettingsService(repo, CreateCache());

        var result = await svc.GetAsync();

        Assert.Equal("Test GmbH", result.OperatorName);
        Assert.Equal(1, repo.GetCallCount);
    }

    [Fact]
    public async Task GetAsync_SecondCall_HitsCacheNotRepository()
    {
        var repo = new StubRepository(DefaultSettings());
        var svc = new SiteSettingsService(repo, CreateCache());

        await svc.GetAsync();
        await svc.GetAsync();

        Assert.Equal(1, repo.GetCallCount);
    }

    [Fact]
    public async Task UpdateAsync_InvalidatesCache_RepositoryCalledAgainOnNextGet()
    {
        var repo = new StubRepository(DefaultSettings());
        var svc = new SiteSettingsService(repo, CreateCache());

        await svc.GetAsync();
        await svc.UpdateAsync(DefaultSettings());
        await svc.GetAsync();

        Assert.Equal(2, repo.GetCallCount);
    }

    [Fact]
    public async Task UpdateAsync_CallsRepository()
    {
        var updated = DefaultSettings() with { OperatorName = "Updated GmbH" };
        var repo = new StubRepository(DefaultSettings());
        var svc = new SiteSettingsService(repo, CreateCache());

        await svc.UpdateAsync(updated);

        Assert.Equal("Updated GmbH", repo.LastUpdated?.OperatorName);
    }

    [Fact]
    public async Task GetAsync_ReturnsCachedValue_AfterFirstLoad()
    {
        var settings = DefaultSettings();
        var repo = new StubRepository(settings);
        var svc = new SiteSettingsService(repo, CreateCache());

        var first = await svc.GetAsync();
        var second = await svc.GetAsync();

        Assert.Same(first, second);
    }
}
