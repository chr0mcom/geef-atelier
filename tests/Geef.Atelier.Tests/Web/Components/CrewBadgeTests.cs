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

    [Fact]
    public void NamedTemplateName_DisplaysName()
    {
        var cut = RenderComponent<CrewBadge>(p => p.Add(c => c.TemplateName, "klassik"));

        var badge = cut.Find("[data-testid='crew-badge']");
        Assert.Equal("klassik", badge.TextContent);
    }

    [Fact]
    public void DataTestId_IsPresent()
    {
        var cut = RenderComponent<CrewBadge>();

        cut.Find("[data-testid='crew-badge']");
    }
}
