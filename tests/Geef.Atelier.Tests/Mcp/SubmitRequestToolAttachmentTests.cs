using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// Unit tests for the optional <c>attachments</c> parameter of <see cref="SubmitRequestTool"/>.
/// </summary>
public sealed class SubmitRequestToolAttachmentTests
{
    private static readonly ICurrentUserService AdminUser = new FakeAdminUser();

    [Fact]
    public async Task SubmitRequest_WithAttachments_DecodesBase64AndPassesToRunService()
    {
        var svc = new CapturingRunService();
        var content = "Hello, attachment!"u8.ToArray();
        var b64 = Convert.ToBase64String(content);

        var json = $$"""
            [{"filename":"doc.md","contentType":"text/markdown","contentBase64":"{{b64}}"}]
            """;

        await SubmitRequestTool.SubmitRequest(svc, AdminUser, "briefing", attachments: json);

        Assert.NotNull(svc.LastAttachments);
        Assert.Single(svc.LastAttachments!);
        Assert.Equal("doc.md",        svc.LastAttachments![0].Filename);
        Assert.Equal("text/markdown",  svc.LastAttachments![0].ContentType);
        Assert.Equal(content,          svc.LastAttachments![0].Content);
    }

    [Fact]
    public async Task SubmitRequest_WithMultipleAttachments_AllDecodedCorrectly()
    {
        var svc  = new CapturingRunService();
        var b64a = Convert.ToBase64String("first"u8.ToArray());
        var b64b = Convert.ToBase64String("second"u8.ToArray());

        var json = $$"""
            [
              {"filename":"a.txt","contentType":"text/plain","contentBase64":"{{b64a}}"},
              {"filename":"b.txt","contentType":"text/plain","contentBase64":"{{b64b}}"}
            ]
            """;

        await SubmitRequestTool.SubmitRequest(svc, AdminUser, "briefing", attachments: json);

        Assert.Equal(2, svc.LastAttachments!.Count);
        Assert.Equal("a.txt", svc.LastAttachments![0].Filename);
        Assert.Equal("b.txt", svc.LastAttachments![1].Filename);
    }

    [Fact]
    public async Task SubmitRequest_WithInvalidJson_ThrowsArgumentException()
    {
        var svc = new CapturingRunService();

        await Assert.ThrowsAsync<ArgumentException>(
            () => SubmitRequestTool.SubmitRequest(svc, AdminUser, "briefing", attachments: "not valid json{{"));
    }

    [Fact]
    public async Task SubmitRequest_WithInvalidBase64_ThrowsArgumentException()
    {
        var svc = new CapturingRunService();

        var json = """[{"filename":"doc.md","contentType":"text/plain","contentBase64":"!!!not-base64!!!"}]""";

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => SubmitRequestTool.SubmitRequest(svc, AdminUser, "briefing", attachments: json));

        Assert.Contains("doc.md", ex.Message);
    }

    [Fact]
    public async Task SubmitRequest_WithUnsupportedContentType_ThrowsArgumentException()
    {
        var svc = new CapturingRunService();
        var b64 = Convert.ToBase64String("binary"u8.ToArray());

        var json = $$"""[{"filename":"doc.docx","contentType":"application/msword","contentBase64":"{{b64}}"}]""";

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => SubmitRequestTool.SubmitRequest(svc, AdminUser, "briefing", attachments: json));

        Assert.Contains("application/msword", ex.Message);
    }

    [Fact]
    public async Task SubmitRequest_OctetStreamContentType_ThrowsArgumentException()
    {
        var svc = new CapturingRunService();
        var b64 = Convert.ToBase64String("binary data"u8.ToArray());

        var json = $$"""[{"filename":"data.bin","contentType":"application/octet-stream","contentBase64":"{{b64}}"}]""";

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => SubmitRequestTool.SubmitRequest(svc, AdminUser, "briefing", attachments: json));

        Assert.Contains("application/octet-stream", ex.Message);
    }

    [Fact]
    public async Task SubmitRequest_WithoutAttachments_PassesNullAttachments()
    {
        var svc = new CapturingRunService();

        await SubmitRequestTool.SubmitRequest(svc, AdminUser, "briefing");

        Assert.Null(svc.LastAttachments);
    }

    [Fact]
    public async Task SubmitRequest_WithEmptyAttachmentsJson_PassesNullAttachments()
    {
        var svc = new CapturingRunService();

        await SubmitRequestTool.SubmitRequest(svc, AdminUser, "briefing", attachments: "[]");

        Assert.Null(svc.LastAttachments);
    }

    // --- Fakes ---

    private sealed class FakeAdminUser : ICurrentUserService
    {
        public string? Username => "admin";
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    private sealed class CapturingRunService : IRunService
    {
        public IReadOnlyList<RunAttachmentInput>? LastAttachments { get; private set; }

        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
        {
            LastAttachments = request.Attachments;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult<RunEntity?>(null);
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RunEntity>>([]);
        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult(false);
        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult<RunDetails?>(null);
        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult<RunWithGroundingViewModel?>(null);
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken ct = default) => Task.FromResult(new WelcomeStats(0, 0, 0, 0, 0, 0));
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult(true);
    }
}
