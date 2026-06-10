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
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
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

    // ── Fakes ──────────────────────────────────────────────────────────────────

    private sealed class StubRunService(RunDetails details) : IRunService
    {
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null,
            string? requestingUsername = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RunEntity>>([details.Run]);
        public Task<Guid> SubmitRunAsync(SubmitRunRequest req, CancellationToken ct = default) =>
            Task.FromResult(Guid.NewGuid());
        public Task<RunEntity?> GetRunAsync(Guid id, string? user, CancellationToken ct = default) =>
            Task.FromResult<RunEntity?>(details.Run);
        public Task<bool> CancelRunAsync(Guid id, string? user, CancellationToken ct = default) =>
            Task.FromResult(false);
        public Task<RunDetails?> GetRunDetailsAsync(Guid id, string? user, CancellationToken ct = default) =>
            Task.FromResult<RunDetails?>(details);
        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid id, string? user, CancellationToken ct = default) =>
            Task.FromResult<RunWithGroundingViewModel?>(new RunWithGroundingViewModel(
                Details: details,
                Snapshot: null,
                GroundedBrief: details.Run.BriefingText,
                GroundingAdvisors: [],
                RecoveryAdvisors: [],
                AdvisorsByIteration: Enumerable.Empty<AdvisorConsultation>().ToLookup(x => x.IterationNumber),
                GroundingConsultations: Array.Empty<GroundingConsultation>(),
                ToolInvocations: []));
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? user, CancellationToken ct = default) =>
            Task.FromResult(new WelcomeStats(0, 0, 0, 0, 0, 0));
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? user, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed class StubArtifactRepository(IReadOnlyList<RunArtifact> artifacts)
        : IRunArtifactRepository
    {
        public Task<IReadOnlyList<RunArtifact>> ListByRunAsync(Guid runId, CancellationToken ct) =>
            Task.FromResult(artifacts);
        public Task<RunArtifact?> GetByIdAsync(Guid artifactId, CancellationToken ct) =>
            Task.FromResult(artifacts.FirstOrDefault(a => a.Id == artifactId));
        public Task<RunArtifact> CreateAsync(RunArtifact a, CancellationToken ct) =>
            Task.FromResult(a);
        public Task DeleteByRunAsync(Guid runId, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class NullLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
