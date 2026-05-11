using Bunit;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Web.Components.UI;
using Microsoft.AspNetCore.Components;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class StatusBadgeTests : TestContext
{
    [Theory]
    [InlineData(RunStatus.Pending,   "badge-pending")]
    [InlineData(RunStatus.Running,   "badge-running")]
    [InlineData(RunStatus.Completed, "badge-completed")]
    [InlineData(RunStatus.Failed,    "badge-failed")]
    [InlineData(RunStatus.Aborted,   "badge-aborted")]
    public void RendersExpectedCssClass(RunStatus status, string expectedClass)
    {
        var cut = RenderComponent<StatusBadge>(p => p.Add(c => c.Status, status));

        Assert.Contains(expectedClass, cut.Find("span").ClassList);
    }

    [Theory]
    [InlineData(RunStatus.Pending)]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Aborted)]
    public void RendersDataStatusAttribute(RunStatus status)
    {
        var cut = RenderComponent<StatusBadge>(p => p.Add(c => c.Status, status));

        Assert.Equal(status.ToString(), cut.Find("span").GetAttribute("data-status"));
    }
}
