using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Runs;

/// <summary>
/// Unit tests for RunService.SubmitRunAsync attachment handling.
/// These tests use in-memory fakes to avoid requiring a Postgres instance.
/// </summary>
public sealed class RunServiceAttachmentTests
{
    // --- Tests ---

    [Fact]
    public async Task SubmitRunAsync_WithAttachments_UploadsRunLocalDocuments()
    {
        var knowledge   = new CapturingKnowledgeService();
        var persistence = new CapturingPersistenceService();
        var svc         = BuildService(persistence: persistence, knowledge: knowledge);

        var attachments = new[]
        {
            new RunAttachmentInput("report.md",  "text/markdown", "# Hello"u8.ToArray()),
            new RunAttachmentInput("notes.txt",  "text/plain",    "some text"u8.ToArray()),
        };

        await svc.SubmitRunAsync(new SubmitRunRequest("briefing", "{}", Attachments: attachments));

        Assert.Equal(2, knowledge.UploadCalls.Count);
        Assert.Equal("report.md", knowledge.UploadCalls[0].Filename);
        Assert.Equal("notes.txt", knowledge.UploadCalls[1].Filename);
    }

    [Fact]
    public async Task SubmitRunAsync_WithAttachments_UsesCorrectRunIdForUpload()
    {
        var knowledge   = new CapturingKnowledgeService();
        var persistence = new CapturingPersistenceService();
        var svc         = BuildService(persistence: persistence, knowledge: knowledge);

        var runId = await svc.SubmitRunAsync(new SubmitRunRequest("briefing", "{}",
            Attachments: [new RunAttachmentInput("f.md", "text/markdown", "x"u8.ToArray())]));

        Assert.All(knowledge.UploadCalls, call => Assert.Equal(runId, call.RunId));
    }

    [Fact]
    public async Task SubmitRunAsync_WithAttachments_PrependsRunAttachmentsProfileToSnapshot()
    {
        var knowledge   = new CapturingKnowledgeService();
        var persistence = new CapturingPersistenceService();
        var svc         = BuildService(persistence: persistence, knowledge: knowledge);

        await svc.SubmitRunAsync(new SubmitRunRequest("briefing", "{}",
            Attachments: [new RunAttachmentInput("doc.md", "text/markdown", "content"u8.ToArray())]));

        Assert.NotNull(persistence.LastUpdatedSnapshotJson);

        var updatedSnapshot = CrewSnapshot.Deserialize(persistence.LastUpdatedSnapshotJson);
        Assert.NotNull(updatedSnapshot);
        Assert.NotNull(updatedSnapshot!.GroundingProviders);
        Assert.NotEmpty(updatedSnapshot.GroundingProviders!);
        Assert.Equal(SystemCrew.RunAttachmentsProfile.Name, updatedSnapshot.GroundingProviders![0].Name);
    }

    [Fact]
    public async Task SubmitRunAsync_WithAttachments_PreservesExistingGroundingProviders()
    {
        var knowledge   = new CapturingKnowledgeService();
        var persistence = new CapturingPersistenceService();
        // Use a custom crew that already has a grounding provider
        var customCrew = new CrewSpec(
            ExecutorProfileName: SystemCrew.DefaultExecutorProfile.Name,
            ReviewerProfileNames: [SystemCrew.BriefingFidelityProfile.Name],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            GroundingProviderNames: [SystemCrew.KnowledgeBaseDefaultProfile.Name]);
        var svc = BuildService(persistence: persistence, knowledge: knowledge);

        await svc.SubmitRunAsync(new SubmitRunRequest("briefing", "{}", CustomCrew: customCrew,
            Attachments: [new RunAttachmentInput("doc.md", "text/markdown", "x"u8.ToArray())]));

        var updatedSnapshot = CrewSnapshot.Deserialize(persistence.LastUpdatedSnapshotJson);
        Assert.NotNull(updatedSnapshot?.GroundingProviders);
        var providerNames = updatedSnapshot!.GroundingProviders!.Select(p => p.Name).ToList();
        Assert.Contains(SystemCrew.RunAttachmentsProfile.Name, providerNames);
        Assert.Contains(SystemCrew.KnowledgeBaseDefaultProfile.Name, providerNames);
        // RunAttachmentsProfile must be first
        Assert.Equal(SystemCrew.RunAttachmentsProfile.Name, providerNames[0]);
    }

    [Fact]
    public async Task SubmitRunAsync_WithoutAttachments_SnapshotUnchanged()
    {
        var persistence = new CapturingPersistenceService();
        var svc         = BuildService(persistence: persistence);

        await svc.SubmitRunAsync(new SubmitRunRequest("briefing", "{}"));

        Assert.Null(persistence.LastUpdatedSnapshotJson);
    }

    [Fact]
    public async Task SubmitRunAsync_WithoutAttachments_KnowledgeServiceNotCalled()
    {
        var knowledge = new CapturingKnowledgeService();
        var svc       = BuildService(knowledge: knowledge);

        await svc.SubmitRunAsync(new SubmitRunRequest("briefing", "{}"));

        Assert.Empty(knowledge.UploadCalls);
    }

    [Fact]
    public async Task SubmitRunAsync_WithEmptyAttachmentList_KnowledgeServiceNotCalled()
    {
        var knowledge   = new CapturingKnowledgeService();
        var persistence = new CapturingPersistenceService();
        var svc         = BuildService(persistence: persistence, knowledge: knowledge);

        await svc.SubmitRunAsync(new SubmitRunRequest("briefing", "{}", Attachments: []));

        Assert.Empty(knowledge.UploadCalls);
        Assert.Null(persistence.LastUpdatedSnapshotJson);
    }

    [Fact]
    public async Task SubmitRunAsync_AttachmentUploadFails_RunMarkedAsFailed()
    {
        var persistence = new CapturingPersistenceService();
        var knowledge   = new FailingKnowledgeService();
        var svc         = BuildService(persistence: persistence, knowledge: knowledge);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SubmitRunAsync(new SubmitRunRequest("briefing", "{}",
                Attachments: [new RunAttachmentInput("doc.md", "text/markdown", "content"u8.ToArray())])));

        Assert.NotNull(persistence.LastFailureMessage);
        Assert.Contains("Attachment upload failed", persistence.LastFailureMessage);
    }

    // --- Builder ---

    private static RunService BuildService(
        CapturingPersistenceService? persistence = null,
        IKnowledgeService?           knowledge   = null)
        => new RunService(
            persistence ?? new CapturingPersistenceService(),
            new StubRunRepository(),
            new StubCrewService(),
            new StubAdvisorConsultationRepository(),
            knowledge ?? new NoOpKnowledgeService(),
            NullLogger<RunService>.Instance);

    // --- Fakes ---

    private sealed class CapturingPersistenceService : IRunPersistenceService
    {
        private Guid _lastRunId = Guid.NewGuid();
        public string? LastUpdatedSnapshotJson { get; private set; }
        public string? LastFailureMessage { get; private set; }

        public Task<Guid> CreateRunAsync(
            string briefingText, string configJson,
            string? createdByUser = null, string? crewTemplateName = null,
            string? crewSnapshotJson = null, RunKind kind = RunKind.Standard,
            CancellationToken cancellationToken = default)
        {
            _lastRunId = Guid.NewGuid();
            return Task.FromResult(_lastRunId);
        }

        public Task UpdateSnapshotAsync(Guid runId, string snapshotJson, CancellationToken cancellationToken = default)
        {
            LastUpdatedSnapshotJson = snapshotJson;
            return Task.CompletedTask;
        }

        public Task MarkRunFailedAsync(Guid runId, string errorMessage, CancellationToken cancellationToken = default)
        {
            LastFailureMessage = errorMessage;
            return Task.CompletedTask;
        }

        public Task<Guid> CreateResumedRunAsync(string briefingText, string configJson,
            string? createdByUser, string? crewTemplateName, string? crewSnapshotJson,
            Guid parentRunId, string? seedDraftText, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());
    }

    private sealed class CapturingKnowledgeService : IKnowledgeService
    {
        public record UploadCall(Guid RunId, string Filename, string ContentType);
        public List<UploadCall> UploadCalls { get; } = [];

        public Task<KnowledgeDocument> UploadRunAttachmentAsync(
            Guid runId, string title, Stream content, string filename,
            string contentType, CancellationToken ct)
        {
            UploadCalls.Add(new UploadCall(runId, filename, contentType));
            return Task.FromResult(MakeDoc(runId));
        }

        // Remaining interface members not needed for these tests.
        public Task<KnowledgeDocument> UploadAsync(string title, string description, IReadOnlyList<string> tags, Stream content, string filename, string contentType, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<KnowledgeDocument?> GetAsync(Guid documentId, CancellationToken ct) => Task.FromResult<KnowledgeDocument?>(null);
        public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct, KnowledgeScope? scope = null) => Task.FromResult<IReadOnlyList<KnowledgeDocument>>([]);
        public Task UpdateMetadataAsync(Guid documentId, string title, string description, IReadOnlyList<string> tags, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;
        public Task ReindexAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;
        public Task ReindexAllAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<KnowledgeDocument>> ListRunAttachmentsAsync(Guid runId, CancellationToken ct) => Task.FromResult<IReadOnlyList<KnowledgeDocument>>([]);
        public Task PromoteToGlobalAsync(Guid documentId, string? newTitle, string? newDescription, IReadOnlyList<string>? additionalTags, CancellationToken ct) => Task.CompletedTask;

        private static KnowledgeDocument MakeDoc(Guid runId)
        {
            var now = DateTimeOffset.UtcNow;
            return new KnowledgeDocument(
                Guid.NewGuid(), "title", "", "file.md", "text/markdown",
                10, "content", [], "model", 1536, 0, null, now, now, KnowledgeScope.RunLocal, runId);
        }
    }

    private sealed class StubRunRepository : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default) => Task.FromResult<RunEntity?>(null);
        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RunEntity>>([]);
        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default) => Task.FromResult<RunDetails?>(null);
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken cancellationToken = default) => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));
        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubCrewService : ICrewService
    {
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<GroundingProviderProfile>? groundingProviders = null;

            if (customCrew?.GroundingProviderNames is { Count: > 0 } names)
            {
                groundingProviders = names
                    .Where(n => SystemCrew.GroundingProviderProfiles.ContainsKey(n))
                    .Select(n => SystemCrew.GroundingProviderProfiles[n])
                    .ToList();
            }

            var snapshot = new CrewSnapshot(
                SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
                TemplateName: crewTemplateName,
                Executor: SystemCrew.DefaultExecutorProfile,
                Reviewers: [SystemCrew.BriefingFidelityProfile],
                EvaluationStrategy: EvaluationStrategy.Parallel,
                ConvergenceOverride: null,
                Advisors: [],
                GroundingProviders: groundingProviders);

            return Task.FromResult(snapshot);
        }

        // Remaining interface members not needed for these tests.
        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<CrewTemplate?>(null);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default) => Task.FromResult(template);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default) => Task.FromResult(template);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomExecutorProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomAdvisorProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomGroundingProviderProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<FinalizerProfile?>(null);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomCrewTemplateAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
    }

    /// <summary>Always throws <see cref="InvalidOperationException"/> to simulate an upload failure.</summary>
    private sealed class FailingKnowledgeService : IKnowledgeService
    {
        public Task<KnowledgeDocument> UploadRunAttachmentAsync(
            Guid runId, string title, Stream content, string filename,
            string contentType, CancellationToken ct)
            => throw new InvalidOperationException("Simulated upload failure");

        public Task<KnowledgeDocument> UploadAsync(string title, string description, IReadOnlyList<string> tags, Stream content, string filename, string contentType, CancellationToken ct)
            => throw new NotImplementedException();
        public Task<KnowledgeDocument?> GetAsync(Guid documentId, CancellationToken ct) => Task.FromResult<KnowledgeDocument?>(null);
        public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct, KnowledgeScope? scope = null) => Task.FromResult<IReadOnlyList<KnowledgeDocument>>([]);
        public Task UpdateMetadataAsync(Guid documentId, string title, string description, IReadOnlyList<string> tags, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;
        public Task ReindexAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;
        public Task ReindexAllAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<KnowledgeDocument>> ListRunAttachmentsAsync(Guid runId, CancellationToken ct) => Task.FromResult<IReadOnlyList<KnowledgeDocument>>([]);
        public Task PromoteToGlobalAsync(Guid documentId, string? newTitle, string? newDescription, IReadOnlyList<string>? additionalTags, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubAdvisorConsultationRepository : IAdvisorConsultationRepository
    {
        public Task<AdvisorConsultation> CreateAsync(AdvisorConsultation consultation, CancellationToken ct) => Task.FromResult(consultation);
        public Task<IReadOnlyList<AdvisorConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct) => Task.FromResult<IReadOnlyList<AdvisorConsultation>>([]);
    }
}
