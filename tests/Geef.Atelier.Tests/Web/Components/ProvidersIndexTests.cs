using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class ProvidersIndexTests : TestContext
{
    private static Provider MakeProvider(
        string name,
        ProviderType type = ProviderType.Http,
        bool isSystem = false,
        bool isActive = true) => new(
        Name: name,
        DisplayName: name + " Display",
        Description: "test provider",
        Type: type,
        Settings: new Dictionary<string, System.Text.Json.JsonElement>(),
        IsSystem: isSystem,
        IsActive: isActive,
        CreatedAt: DateTimeOffset.UtcNow,
        UpdatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void ShowsProviderTable_WhenProvidersExist()
    {
        Services.AddSingleton<IProviderService>(new StubProviderService([MakeProvider("openrouter", isSystem: true)]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<ProvidersIndex>();

        cut.Find("[data-testid='providers-table']");
    }

    [Fact]
    public void EmptyList_ShowsNoTableText()
    {
        Services.AddSingleton<IProviderService>(new StubProviderService([]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<ProvidersIndex>();

        Assert.Contains("No providers configured yet.", cut.Markup);
    }

    [Fact]
    public void SystemProvider_RowHasViewLink_NotDeleteButton()
    {
        var provider = MakeProvider("openrouter", isSystem: true);
        Services.AddSingleton<IProviderService>(new StubProviderService([provider]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<ProvidersIndex>();
        var row = cut.Find("[data-testid='provider-row-openrouter']");
        var viewLink = row.QuerySelector("a[href*='/view/openrouter']");
        var deleteBtn = row.QuerySelector("[data-testid='delete-openrouter']");

        Assert.NotNull(viewLink);
        Assert.Null(deleteBtn);
    }

    [Fact]
    public void CustomProvider_HasDeleteButton()
    {
        var provider = MakeProvider("custom-myprovider", isSystem: false);
        Services.AddSingleton<IProviderService>(new StubProviderService([provider]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<ProvidersIndex>();
        var deleteBtn = cut.Find("[data-testid='delete-custom-myprovider']");

        Assert.NotNull(deleteBtn);
    }

    [Fact]
    public void CustomProvider_HasToggleButton()
    {
        var provider = MakeProvider("custom-myprovider", isSystem: false);
        Services.AddSingleton<IProviderService>(new StubProviderService([provider]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<ProvidersIndex>();
        var toggleBtn = cut.Find("[data-testid='toggle-custom-myprovider']");

        Assert.NotNull(toggleBtn);
    }

    [Fact]
    public void ProviderType_Http_ShowsHttpLabel()
    {
        var provider = MakeProvider("openrouter", ProviderType.Http, isSystem: true);
        Services.AddSingleton<IProviderService>(new StubProviderService([provider]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<ProvidersIndex>();

        Assert.Contains("HTTP", cut.Markup);
    }

    [Fact]
    public void ProviderType_Cli_ShowsCliLabel()
    {
        var provider = MakeProvider("claude-cli", ProviderType.Cli, isSystem: true);
        Services.AddSingleton<IProviderService>(new StubProviderService([provider]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<ProvidersIndex>();

        Assert.Contains("CLI", cut.Markup);
    }

    [Fact]
    public void DeleteButton_Click_ShowsDeleteConfirmationModal()
    {
        var provider = MakeProvider("custom-deleteme", isSystem: false);
        Services.AddSingleton<IProviderService>(new StubProviderService([provider]));
        this.AddTestAuthorization().SetAuthorized("test-user");

        var cut = RenderComponent<ProvidersIndex>();
        var deleteBtn = cut.Find("[data-testid='delete-custom-deleteme']");
        deleteBtn.Click();

        // DeleteConfirmationModal should now appear
        var modal = cut.Find("[data-testid='delete-confirm-input']");
        Assert.NotNull(modal);
    }

    // ── Stub ──────────────────────────────────────────────────────────────────

    private sealed class StubProviderService(IReadOnlyList<Provider> providers) : IProviderService
    {
        public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
            => Task.FromResult(providers);

        public Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(providers.FirstOrDefault(p => p.Name == name));

        public Task<Provider> CreateCustomAsync(Provider provider, CancellationToken ct = default)
            => Task.FromResult(provider);

        public Task<Provider> UpdateCustomAsync(string name, Provider provider, CancellationToken ct = default)
            => Task.FromResult(provider);

        public Task DeleteCustomAsync(string name, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default)
            => Task.FromResult(new ConnectionTestResult(true, 42, null, null));
    }
}
