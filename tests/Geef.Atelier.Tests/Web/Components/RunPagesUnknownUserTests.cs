using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// The run list and the run page refuse a sign-in without a user name instead of showing the runs of every user: no
/// data service and no live update is used, the page asks to sign in again.
/// </summary>
public sealed class RunPagesUnknownUserTests : TestContext
{
    private readonly TestAuthorizationContext _auth;

    public RunPagesUnknownUserTests()
    {
        Services.AddSingleton(NeverCalled<IRunService>.Create());
        Services.AddSingleton(NeverCalled<IRunUpdateFeed>.Create());
        Services.AddSingleton(NeverCalled<IRunArtifactRepository>.Create());
        Services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        Services.AddScoped<CircuitDataGate>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _auth = this.AddTestAuthorization();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_run_list_asks_to_sign_in_again(bool withEmptyName)
    {
        SignIn(withEmptyName);

        var cut = RenderComponent<Runs>();

        Assert.Contains(OwnerScope.UnknownUserMessage, cut.Find("[data-testid='runs-unknown-user']").TextContent);
        Assert.Empty(cut.FindAll(".run-row-wrapper"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_run_page_asks_to_sign_in_again(bool withEmptyName)
    {
        SignIn(withEmptyName);

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, Guid.NewGuid()));

        Assert.Contains(OwnerScope.UnknownUserMessage, cut.Find("[data-testid='run-unknown-user']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='delete-run-button']"));
    }

    private void SignIn(bool withEmptyName)
    {
        if (withEmptyName)
            _auth.SetAuthorized(string.Empty);
        else
            this.SignInWithoutUserName();
    }
}
