using System.Reflection;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Profile;

/// <summary>"About me": the person's own profile, saved once and used in every text; only ever the signed-in user's.</summary>
public sealed class AboutMeTests : TestContext
{
    private readonly FakeProfileService _profiles = new();
    private readonly TestAuthorizationContext _auth;

    public AboutMeTests()
    {
        Services.AddSingleton<IProfileService>(_profiles);
        Services.AddScoped<CircuitDataGate>();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _auth = this.AddTestAuthorization();
        _auth.SetAuthorized("alice");
    }

    [Fact]
    public void ThePageNeedsASignInAndHasItsRoute()
    {
        Assert.Contains(typeof(AboutMe).GetCustomAttributes<RouteAttribute>(), r => r.Template == "/about-me");
        Assert.Null(Assert.Single(typeof(AboutMe).GetCustomAttributes<AuthorizeAttribute>(inherit: true)).Policy);
    }

    [Fact]
    public void ShowsTheStoredProfileOfTheSignedInPerson()
    {
        _profiles.Set("alice", new UserProfileInput("Alice Example", "Teacher", "Example School", Formality.Informal, "de", "Viele Grüße, Alice", "No emojis", "I write for parents."));
        _profiles.Set("bob", new UserProfileInput("Bob", null, null, Formality.Formal, null, null, null, null));

        var cut = RenderComponent<AboutMe>();

        Assert.Equal("alice", Assert.Single(_profiles.Loaded));
        Assert.Equal("Alice Example", cut.Find("[data-testid='about-name']").GetAttribute("value"));
        Assert.Equal("Teacher", cut.Find("[data-testid='about-role']").GetAttribute("value"));
        Assert.Equal("Example School", cut.Find("[data-testid='about-organization']").GetAttribute("value"));
        Assert.True(cut.Find("[data-testid='about-formality-informal']").HasAttribute("checked"));
        Assert.False(cut.Find("[data-testid='about-formality-formal']").HasAttribute("checked"));
        Assert.Equal("de", cut.Find("[data-testid='about-language']").GetAttribute("value"));
        Assert.Equal("Viele Grüße, Alice", cut.Find("[data-testid='about-signature']").GetAttribute("value"));
        Assert.Equal("No emojis", cut.Find("[data-testid='about-nogos']").GetAttribute("value"));
        Assert.Equal("I write for parents.", cut.Find("[data-testid='about-freetext']").GetAttribute("value"));
        Assert.DoesNotContain("Bob", cut.Markup);
    }

    [Fact]
    public void TheFormalityChoicesArePlain()
    {
        var cut = RenderComponent<AboutMe>();

        var labels = cut.FindAll("[data-testid='about-formality'] .about-radio").Select(l => l.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "Formal (Sie)", "Informal (du)", "It depends" }, labels);
        Assert.True(cut.Find("[data-testid='about-formality-unset']").HasAttribute("checked"));
    }

    [Fact]
    public void SaveStoresTheValuesForTheOwnName()
    {
        var cut = RenderComponent<AboutMe>();

        cut.Find("[data-testid='about-name']").Input("Alice Example");
        cut.Find("[data-testid='about-role']").Input("Head of the parents' council");
        cut.Find("[data-testid='about-formality-formal']").Change(true);
        cut.Find("[data-testid='about-language']").Change("en");
        cut.Find("[data-testid='about-signature']").Input("Best wishes, Alice");
        cut.Find("[data-testid='about-save']").Click();

        var (username, input) = Assert.Single(_profiles.Saved);
        Assert.Equal("alice", username);
        Assert.Equal(new UserProfileInput("Alice Example", "Head of the parents' council", null, Formality.Formal, "en", "Best wishes, Alice", null, null), input);
        Assert.Contains("Saved.", cut.Find("[data-testid='about-saved']").TextContent);
    }

    [Fact]
    public void ThereIsAChoiceForTheLanguageOfTheRequest()
    {
        var cut = RenderComponent<AboutMe>();

        cut.Find("[data-testid='about-save']").Click();

        Assert.Null(Assert.Single(_profiles.Saved).Input.Language);
        Assert.Equal("The language of my request", cut.Find("[data-testid='about-language'] option[value='']").TextContent);
    }

    [Fact]
    public void AnUnknownStoredLanguageStaysSelectable()
    {
        _profiles.Set("alice", FakeProfileService.Empty with { Language = "sv" });

        var cut = RenderComponent<AboutMe>();

        Assert.Equal("sv", cut.Find("[data-testid='about-language']").GetAttribute("value"));
        Assert.NotNull(cut.Find("[data-testid='about-language'] option[value='sv']"));
    }

    [Fact]
    public void TheFieldsHaveTheLimitsOfTheProfile()
    {
        var cut = RenderComponent<AboutMe>();

        Assert.Equal(UserProfileLimits.DisplayName.ToString(), cut.Find("[data-testid='about-name']").GetAttribute("maxlength"));
        Assert.Equal(UserProfileLimits.Role.ToString(), cut.Find("[data-testid='about-role']").GetAttribute("maxlength"));
        Assert.Equal(UserProfileLimits.Organization.ToString(), cut.Find("[data-testid='about-organization']").GetAttribute("maxlength"));
        Assert.Equal(UserProfileLimits.Signature.ToString(), cut.Find("[data-testid='about-signature']").GetAttribute("maxlength"));
        Assert.Equal(UserProfileLimits.NoGos.ToString(), cut.Find("[data-testid='about-nogos']").GetAttribute("maxlength"));
        Assert.Equal(UserProfileLimits.FreeText.ToString(), cut.Find("[data-testid='about-freetext']").GetAttribute("maxlength"));
    }

    [Fact]
    public void ATooLongValueShowsTheMessageOfTheServiceAndKeepsTheEntries()
    {
        _profiles.SaveError = new ProfileValidationException("“Anything else I should know” can have up to 4,000 characters.");
        var cut = RenderComponent<AboutMe>();

        cut.Find("[data-testid='about-freetext']").Input("long text");
        cut.Find("[data-testid='about-save']").Click();

        Assert.Contains("can have up to 4,000 characters", cut.Find("[data-testid='about-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='about-saved']"));
        Assert.Equal("long text", cut.Find("[data-testid='about-freetext']").GetAttribute("value"));
    }

    [Fact]
    public void AnUnexpectedErrorDoesNotShowItsDetails()
    {
        _profiles.SaveError = new InvalidOperationException("connection refused");
        var cut = RenderComponent<AboutMe>();

        cut.Find("[data-testid='about-save']").Click();

        Assert.Contains("Try again in a moment.", cut.Find("[data-testid='about-error']").TextContent);
        Assert.DoesNotContain("connection refused", cut.Markup);
    }

    [Fact]
    public void ClearMyProfileAsksFirst()
    {
        _profiles.Set("alice", FakeProfileService.Empty with { DisplayName = "Alice" });
        var cut = RenderComponent<AboutMe>();

        cut.Find("[data-testid='about-clear']").Click();
        Assert.Empty(_profiles.Cleared);
        Assert.Contains("This deletes everything on this page.", cut.Find("[data-testid='about-clear-confirm']").TextContent);

        cut.Find("[data-testid='about-clear-cancel']").Click();
        Assert.Empty(_profiles.Cleared);
        Assert.Empty(cut.FindAll("[data-testid='about-clear-confirm']"));

        cut.Find("[data-testid='about-clear']").Click();
        cut.Find("[data-testid='about-clear-yes']").Click();

        Assert.Equal("alice", Assert.Single(_profiles.Cleared));
        Assert.Null(_profiles.Stored("alice"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='about-name']").GetAttribute("value") ?? string.Empty);
        Assert.NotNull(cut.Find("[data-testid='about-cleared']"));
    }

    [Fact]
    public void AnAdministratorSeesOnlyTheirOwnProfile()
    {
        _auth.SetAuthorized("root");
        _auth.SetRoles(OwnerScope.AdminRole);
        _profiles.Set("alice", FakeProfileService.Empty with { DisplayName = "Alice" });

        var cut = RenderComponent<AboutMe>();

        Assert.Equal("root", Assert.Single(_profiles.Loaded));
        Assert.DoesNotContain("Alice", cut.Markup);
    }

    [Fact]
    public void WithoutAUserNameNothingIsLoaded()
    {
        _auth.SetAuthorized(string.Empty);

        var cut = RenderComponent<AboutMe>();

        Assert.Contains(OwnerScope.UnknownUserMessage, cut.Markup);
        Assert.Empty(_profiles.Loaded);
        Assert.Empty(cut.FindAll("[data-testid='about-save']"));
    }

    [Fact]
    public void ALoadErrorStillAllowsFillingIn()
    {
        _profiles.GetError = new InvalidOperationException("db");

        var cut = RenderComponent<AboutMe>();

        Assert.NotNull(cut.Find("[data-testid='about-load-failed']"));
        Assert.NotNull(cut.Find("[data-testid='about-save']"));
    }

    [Fact]
    public void TheUserMenuLeadsToAboutMeBeforeConnectedClients()
    {
        Services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor());
        var cut = RenderComponent<UserMenu>();

        cut.Find(".user-chip").Click();

        var entries = cut.FindAll(".user-menu-dd a.item").Select(a => a.GetAttribute("data-testid")).ToList();
        Assert.Equal("/about-me", cut.Find("[data-testid='menu-about-me']").GetAttribute("href"));
        Assert.Equal("About me", cut.Find("[data-testid='menu-about-me']").TextContent.Trim());
        Assert.True(entries.IndexOf("menu-about-me") < entries.IndexOf("menu-connected-clients"));
        Assert.DoesNotContain("Coming soon", cut.Markup);
    }
}
