using Bunit;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class DomainBadgeTests : Bunit.TestContext
{
    [Theory]
    [InlineData(ModelDomain.Correspondence, "Correspondence", "domain-badge--correspondence")]
    [InlineData(ModelDomain.Speech, "Speech", "domain-badge--speech")]
    [InlineData(ModelDomain.Technical, "Technical", "domain-badge--technical")]
    [InlineData(ModelDomain.Legal, "Legal", "domain-badge--legal")]
    [InlineData(ModelDomain.Academic, "Academic", "domain-badge--academic")]
    [InlineData(ModelDomain.Marketing, "Marketing", "domain-badge--marketing")]
    public void ExplicitDomain_IsShownWithItsLabelAndClass(ModelDomain domain, string label, string cssClass)
    {
        var cut = RenderComponent<DomainBadge>(p => p.Add(c => c.Domain, domain));

        var badge = cut.Find("[data-testid='domain-badge']");
        Assert.Equal(label, badge.TextContent.Trim());
        Assert.Contains(cssClass, badge.ClassList);
    }

    [Fact]
    public void ExplicitDomain_TakesPrecedenceOverTheNameMapping()
    {
        var cut = RenderComponent<DomainBadge>(p => p
            .Add(c => c.TemplateName, "juristisch")
            .Add(c => c.Domain, ModelDomain.Technical));

        Assert.Equal("Technical", cut.Find("[data-testid='domain-badge']").TextContent.Trim());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ModelDomain.General)]
    public void NoDomainOrGeneral_FallsBackToTheNameMapping(ModelDomain? domain)
    {
        var cut = RenderComponent<DomainBadge>(p => p
            .Add(c => c.TemplateName, "akademisch")
            .Add(c => c.Domain, domain));

        Assert.Equal("Academic", cut.Find("[data-testid='domain-badge']").TextContent.Trim());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ModelDomain.General)]
    public void NoDomainAndGenericName_RendersNothing(ModelDomain? domain)
    {
        var cut = RenderComponent<DomainBadge>(p => p
            .Add(c => c.TemplateName, "standard")
            .Add(c => c.Domain, domain));

        Assert.Empty(cut.FindAll("[data-testid='domain-badge']"));
    }
}
