using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// The editor for HTTP providers was removed in stage 8c (D-089): HTTP providers come with the system and are read-only, so no
/// page links to the removed routes any more, while CLI providers keep their editor and can still be copied.
/// </summary>
public sealed class ProviderPagesWithoutHttpEditorTests : TestContext
{
    [Fact]
    public void The_type_selection_offers_only_the_cli_provider()
    {
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("admin");

        var cut = RenderComponent<ProviderTypeSelection>();

        Assert.Empty(cut.FindAll("[data-testid='type-http']"));
        Assert.DoesNotContain("/crew/providers/create/http", cut.Markup);
        cut.Find("[data-testid='type-cli']");
    }

    [Fact]
    public void A_system_http_provider_can_neither_be_copied_nor_edited()
    {
        Services.AddSingleton<IProviderService>(new FakeProviderService());
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("admin");

        var cut = RenderComponent<ProviderView>(p => p.Add(v => v.Name, SystemProviders.OpenRouter.Name));

        Assert.Empty(cut.FindAll("[data-testid='btn-clone']"));
        Assert.DoesNotContain("/crew/providers/edit/", cut.Markup);
    }

    [Fact]
    public void A_system_cli_provider_can_still_be_copied()
    {
        Services.AddSingleton<IProviderService>(new FakeProviderService());
        this.AddTestAuthorization().SetAuthorized("admin").SetRoles("admin");

        var cut = RenderComponent<ProviderView>(p => p.Add(v => v.Name, SystemProviders.ClaudeCli.Name));

        cut.Find("[data-testid='btn-clone']");
    }
}
