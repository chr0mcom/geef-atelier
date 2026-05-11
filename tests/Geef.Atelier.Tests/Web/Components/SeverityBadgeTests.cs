using Bunit;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class SeverityBadgeTests : TestContext
{
    [Theory]
    [InlineData(FindingSeverity.Critical, "severity-critical")]
    [InlineData(FindingSeverity.Major,    "severity-major")]
    [InlineData(FindingSeverity.Minor,    "severity-minor")]
    [InlineData(FindingSeverity.Info,     "severity-info")]
    public void RendersExpectedCssClass(FindingSeverity severity, string expectedClass)
    {
        var cut = RenderComponent<SeverityBadge>(p => p.Add(c => c.Severity, severity));

        Assert.Contains(expectedClass, cut.Find("span").ClassList);
    }
}
