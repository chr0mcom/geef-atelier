using Bunit;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Web.Components.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class CrewSelectorTests : TestContext
{
    private static CrewTemplate MakeTemplate(string name, string displayName, bool isSystem = false) => new(
        Name: name,
        DisplayName: displayName,
        Description: "",
        ExecutorProfileName: "default-executor",
        ReviewerProfileNames: Array.Empty<string>(),
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        AdvisorProfileNames: Array.Empty<string>(),
        IsSystem: isSystem);

    private void RegisterCrewService(IReadOnlyList<CrewTemplate> templates)
    {
        Services.AddSingleton<ICrewService>(new StubCrewService(templates));
    }

    [Fact]
    public void RendersSelectWithDataTestId()
    {
        RegisterCrewService([MakeTemplate("klassik", "Klassik", isSystem: true)]);
        var cut = RenderComponent<CrewSelector>();

        cut.Find("[data-testid='crew-selector']");
    }

    [Fact]
    public void DefaultValue_IsKlassik_WhenKlassikExistsInList()
    {
        RegisterCrewService(
        [
            MakeTemplate("other", "Other"),
            MakeTemplate(SystemCrew.KlassikTemplateName, "Klassik", isSystem: true),
        ]);

        var cut = RenderComponent<CrewSelector>();

        var select = cut.Find("[data-testid='crew-selector']");
        Assert.Equal(SystemCrew.KlassikTemplateName, select.GetAttribute("value")
            ?? cut.Instance.Value);
    }

    [Fact]
    public void SelectingDifferentOption_UpdatesValueOnComponent()
    {
        RegisterCrewService(
        [
            MakeTemplate(SystemCrew.KlassikTemplateName, "Klassik", isSystem: true),
            MakeTemplate("custom-crew", "My Custom Crew"),
        ]);

        var cut = RenderComponent<CrewSelector>(p =>
            p.Add(c => c.Value, SystemCrew.KlassikTemplateName));

        cut.Find("[data-testid='crew-selector']").Change("custom-crew");

        // After the user changes the select, the component's own Value should update
        Assert.Equal("custom-crew", cut.Instance.Value);
    }

    [Fact]
    public void WithEmptyTemplateList_RendersEmptySelect_NocrashOccurs()
    {
        RegisterCrewService([]);
        var cut = RenderComponent<CrewSelector>();

        cut.Find("[data-testid='crew-selector']");
    }

    private sealed class StubCrewService(IReadOnlyList<CrewTemplate> templates) : ICrewService
    {
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult(templates);

        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default) => Task.FromResult<CrewTemplate?>(null);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken ct = default) => Task.FromResult(template);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken ct = default) => Task.FromResult(template);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? name, CrewSpec? spec, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, "klassik", SystemCrew.DefaultExecutorProfile, [], EvaluationStrategy.Parallel, null, []));
    }
}
