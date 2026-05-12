using Bunit;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class SeverityBadgeTests : TestContext
{
    [Theory]
    [InlineData(FindingSeverity.Critical, "critical")]
    [InlineData(FindingSeverity.Major,    "major")]
    [InlineData(FindingSeverity.Minor,    "minor")]
    [InlineData(FindingSeverity.Info,     "info")]
    public void RendersExpectedCssClass(FindingSeverity severity, string expectedClass)
    {
        var cut = RenderComponent<SeverityBadge>(p => p.Add(c => c.Severity, severity));

        Assert.Contains(expectedClass, cut.Find("span").ClassList);
    }
}
