using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application_IKnowledgeService = Geef.Atelier.Application.Crew.Knowledge.IKnowledgeService;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class RunDetailArtifactsTests : TestContext
{
    private readonly Guid _runId = Guid.NewGuid();

    private void RegisterServices(
        RunDetails? details = null,
        IReadOnlyList<RunArtifact>? artifacts = null)
    {
        var effectiveDetails = details ?? new RunDetails(
            new RunEntity
            {
                Id = _runId,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = RunStatus.Completed,
                BriefingText = "Test briefing",
                ConfigJson = "{}",
                FinalText = "# Final\n\nContent.",
            },
            []);

        Services.AddSingleton<IRunService>(new StubRunService(effectiveDetails));
        Services.AddSingleton<IRunArtifactRepository>(
            new StubArtifactRepository(artifacts ?? []));
        Services.AddSingleton<Application_IKnowledgeService>(new NoOpKnowledgeService());
        Services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        this.AddTestAuthorization().SetAuthorized("user");
    }

    [Fact]
    public void NoArtifacts_ArtifactsSectionNotRendered()
    {
        RegisterServices(artifacts: []);

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        Assert.Throws<Bunit.ElementNotFoundException>(
            () => cut.Find("[data-testid='artifacts-section']"));
    }

    [Fact]
    public void WithFileArtifact_ArtifactsSectionRendered()
    {
        var artifact = new RunArtifact
        {
            Id = Guid.NewGuid(),
            RunId = _runId,
            FinalizerProfileName = "export-markdown",
            ArtifactType = ArtifactType.File,
            Filename = "document.md",
            ContentType = "text/markdown",
            SizeBytes = 512,
            StorageUri = "/app/exports/abc/document.md",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        RegisterServices(artifacts: [artifact]);

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        cut.Find("[data-testid='artifacts-section']");
    }

    [Fact]
    public void WithFileArtifact_DownloadLinkRendered()
    {
        var artifact = new RunArtifact
        {
            Id = Guid.NewGuid(),
            RunId = _runId,
            FinalizerProfileName = "export-markdown",
            ArtifactType = ArtifactType.File,
            Filename = "document.md",
            ContentType = "text/markdown",
            SizeBytes = 512,
            StorageUri = "/app/exports/abc/document.md",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        RegisterServices(artifacts: [artifact]);

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        var downloadLink = cut.Find($"[data-testid='artifact-download-{artifact.Id}']");
        Assert.Contains($"/runs/{_runId}/artifacts/{artifact.Id}/download",
            downloadLink.GetAttribute("href"));
    }

    [Fact]
    public void WithStatusArtifact_DownloadsSectionNotRendered()
    {
        // Status artifacts show in FinalizersPipeline, not the downloads table.
        var artifact = new RunArtifact
        {
            Id = Guid.NewGuid(),
            RunId = _runId,
            FinalizerProfileName = "export-pdf",
            ArtifactType = ArtifactType.Status,
            StorageUri = "error",
            StatusMessage = "Export failed: disk full",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        RegisterServices(artifacts: [artifact]);

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        Assert.Throws<Bunit.ElementNotFoundException>(
            () => cut.Find("[data-testid='artifacts-section']"));
        Assert.Throws<Bunit.ElementNotFoundException>(
            () => cut.Find($"[data-testid='artifact-download-{artifact.Id}']"));
    }
}
