using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class CrewTemplateEditorDomainTests : Bunit.TestContext
{
    private static CrewTemplate Template(ModelDomain? domain, bool isSystem = false) => new(
        Name: "custom-t",
        DisplayName: "T",
        Description: "d",
        ExecutorProfileName: "default-executor",
        ReviewerProfileNames: [],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        AdvisorProfileNames: [],
        GroundingProviderNames: [],
        IsSystem: isSystem,
        Domain: domain);

    private CapturingCrewService Wire(CrewTemplate? template)
    {
        var crew = new CapturingCrewService(template);
        Services.AddSingleton<ICrewService>(crew);
        Services.AddSingleton<IOptions<ConvergenceOptions>>(Options.Create(new ConvergenceOptions
        {
            MaxIterations = 3, AbortOnCritical = false, DetectRegression = true, StagnationThreshold = 3,
        }));
        Services.AddSingleton<ISpecializationPackService>(new NoPacks());
        this.AddTestAuthorization().SetAuthorized("user");
        return crew;
    }

    [Fact]
    public void Editor_OffersTheSevenDomains()
    {
        Wire(Template(ModelDomain.Legal));

        var cut = RenderComponent<CrewTemplateEditor>(p => p.Add(c => c.Name, "custom-t"));

        var options = cut.FindAll("[data-testid='template-domain-select'] option").Select(o => o.GetAttribute("value")!).ToList();
        Assert.Equal(Enum.GetNames<ModelDomain>(), options);
        Assert.Equal(7, options.Count);
    }

    [Fact]
    public void Editor_ExplainsTheDomainWithFieldHelp()
    {
        Wire(Template(ModelDomain.Legal));

        var cut = RenderComponent<CrewTemplateEditor>(p => p.Add(c => c.Name, "custom-t"));

        Assert.Contains("Subject area of the crew", cut.Find("small.field-help[data-testid='template-domain-help']").TextContent);
    }

    [Fact]
    public void Editor_PreselectsTheStoredDomain()
    {
        Wire(Template(ModelDomain.Academic));

        var cut = RenderComponent<CrewTemplateEditor>(p => p.Add(c => c.Name, "custom-t"));

        Assert.Equal("Academic", cut.Find("[data-testid='template-domain-select']").GetAttribute("value"));
    }

    [Fact]
    public void Editor_ShowsANullDomainAsGeneral()
    {
        Wire(Template(null));

        var cut = RenderComponent<CrewTemplateEditor>(p => p.Add(c => c.Name, "custom-t"));

        Assert.Equal("General", cut.Find("[data-testid='template-domain-select']").GetAttribute("value"));
    }

    [Fact]
    public void NewTemplate_DefaultsToGeneral()
    {
        Wire(null);

        var cut = RenderComponent<CrewTemplateEditor>();

        Assert.Equal("General", cut.Find("[data-testid='template-domain-select']").GetAttribute("value"));
    }

    [Fact]
    public async Task Save_PersistsTheChosenDomain()
    {
        var crew = Wire(Template(ModelDomain.General));
        var cut = RenderComponent<CrewTemplateEditor>(p => p.Add(c => c.Name, "custom-t"));

        cut.Find("[data-testid='template-domain-select']").Change(nameof(ModelDomain.Technical));
        await cut.Find("[data-testid='template-editor-form']").SubmitAsync();

        Assert.Equal(ModelDomain.Technical, crew.LastSaved?.Domain);
    }

    [Fact]
    public async Task Save_WithoutChange_KeepsTheStoredDomain()
    {
        var crew = Wire(Template(ModelDomain.Marketing));
        var cut = RenderComponent<CrewTemplateEditor>(p => p.Add(c => c.Name, "custom-t"));

        await cut.Find("[data-testid='template-editor-form']").SubmitAsync();

        Assert.Equal(ModelDomain.Marketing, crew.LastSaved?.Domain);
    }

    [Fact]
    public void View_ShowsTheDomainAsBadge()
    {
        Wire(Template(ModelDomain.Legal, isSystem: true));

        var cut = RenderComponent<CrewTemplateView>(p => p.Add(c => c.Name, "custom-t"));

        var badge = cut.Find("[data-testid='detail-domain'] [data-testid='domain-badge']");
        Assert.Equal("Legal", badge.TextContent.Trim());
        Assert.Contains("domain-badge--legal", badge.ClassList);
    }

    [Fact]
    public void View_ShowsANullDomainAsGeneral()
    {
        Wire(Template(null, isSystem: true));

        var cut = RenderComponent<CrewTemplateView>(p => p.Add(c => c.Name, "custom-t"));

        Assert.Equal("General", cut.Find("[data-testid='detail-domain']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='domain-badge']"));
    }

    [Fact]
    public void Index_ShowsABadgeForTheStoredDomain_AndNoneForGeneral()
    {
        var crew = Wire(Template(ModelDomain.Speech));
        crew.Listed = [Template(ModelDomain.Speech) with { Name = "custom-speech" }, Template(null) with { Name = "custom-plain" }];

        var cut = RenderComponent<CrewTemplatesIndex>();

        Assert.Equal("Speech", cut.Find("[data-testid='template-row-custom-speech'] [data-testid='domain-badge']").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid='template-row-custom-plain'] [data-testid='domain-badge']"));
    }

    private sealed class CapturingCrewService(CrewTemplate? template) : StubCrewServiceBase
    {
        public CrewTemplate? LastSaved { get; private set; }
        public IReadOnlyList<CrewTemplate> Listed { get; set; } = [];

        public override Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult(Listed);

        public override Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default)
            => Task.FromResult(template?.Name == name ? template : null);

        public override Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default)
        {
            LastSaved = t;
            return Task.FromResult(t);
        }

        public override Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default)
        {
            LastSaved = t;
            return Task.FromResult(t);
        }
    }

    private sealed class NoPacks : ISpecializationPackService
    {
        public Task<IReadOnlyList<SpecializationPack>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SpecializationPack>>([]);
        public Task<SpecializationPack?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<SpecializationPack?>(null);
        public Task<IReadOnlyList<SpecializationPack>> ListForBindingAsync(
            PackActorType actorType, string? crewDomain, string? owningCrewId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SpecializationPack>>([]);
        public Task SaveAsync(SpecializationPack pack, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> FindReferencingTemplatesAsync(string packName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<GeneralityReviewResult> PromoteAsync(string name, PackScope targetScope, string? targetDomain, CancellationToken ct = default)
            => Task.FromResult(new GeneralityReviewResult(true, []));
        public Task DemoteAsync(string name, string targetDomain, CancellationToken ct = default) => Task.CompletedTask;
        public Task<GeneralityReviewResult> CloneToGeneralizeAsync(string sourceName, string newName, PackScope targetScope, string? targetDomain, CancellationToken ct = default)
            => Task.FromResult(new GeneralityReviewResult(true, []));
    }
}
