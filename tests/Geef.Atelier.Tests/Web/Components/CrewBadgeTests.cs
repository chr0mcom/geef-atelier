using Bunit;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class CrewBadgeTests : TestContext
{
    [Fact]
    public void NullTemplateName_DisplaysCustom()
    {
        var cut = RenderComponent<CrewBadge>(p => p.Add(c => c.TemplateName, (string?)null));

        var badge = cut.Find("[data-testid='crew-badge']");
        Assert.Equal("custom", badge.TextContent);
    }

    [Theory]
    [InlineData("direct", "Direct")]
    [InlineData("unknown-setup", "unknown-setup")]
    public void NamedTemplateName_DisplaysItsDisplayNameOrTheStoredName(string template, string shown)
    {
        var cut = RenderComponent<CrewBadge>(p => p.Add(c => c.TemplateName, template));

        var badge = cut.Find("[data-testid='crew-badge']");
        Assert.Equal(shown, badge.TextContent);
    }

    [Fact]
    public void DataTestId_IsPresent()
    {
        var cut = RenderComponent<CrewBadge>();

        cut.Find("[data-testid='crew-badge']");
    }
}
