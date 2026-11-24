using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

public sealed class SubmitAutoCrewRequestToolTests
{
    private readonly CapturingRunService _runs = new();
    private readonly FakeUser _alice = new("alice");

    [Fact]
    public async Task The_old_path_marks_its_run_as_coming_from_mcp()
    {
        var dto = await SubmitAutoCrewRequestTool.SubmitAutoCrewRequest(_runs, _alice, "Write a short note.");

        var request = Assert.Single(_runs.Requests);
        Assert.Equal(OrderEntry.Mcp, request.Entry);
        Assert.True(request.AutoCompose);
        Assert.Equal("alice", request.CreatedByUser);
        Assert.Null(request.Attachments);
        Assert.Equal("Pending", dto.Status);
    }

    [Fact]
    public async Task Attachments_are_checked_by_the_shared_parser_and_passed_on()
    {
        var json = Attachments.Json(("offer.pdf", "application/pdf", Attachments.MinimalPdf));

        await SubmitAutoCrewRequestTool.SubmitAutoCrewRequest(_runs, _alice, "Summarise the offer.", attachments: json);

        var attachment = Assert.Single(Assert.Single(_runs.Requests).Attachments!);
        Assert.Equal("offer.pdf", attachment.Filename);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(Attachments.MinimalPdf, attachment.Content);
    }

    [Fact]
    public async Task A_placeholder_attachment_is_rejected()
    {
        var json = Attachments.Text("full.md", "See attached full version.", "text/markdown");

        await Assert.ThrowsAsync<McpException>(() =>
            SubmitAutoCrewRequestTool.SubmitAutoCrewRequest(_runs, _alice, "Summarise it.", attachments: json));

        Assert.Empty(_runs.Requests);
    }

    [Fact]
    public async Task A_word_document_is_rejected_with_a_pointer_to_submit_order()
    {
        var json = Attachments.Json(("letter.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Attachments.MinimalDocx));

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            SubmitAutoCrewRequestTool.SubmitAutoCrewRequest(_runs, _alice, "Summarise it.", attachments: json));

        Assert.Contains("submit_order", ex.Message);
        Assert.Empty(_runs.Requests);
    }

    private sealed class CapturingRunService : IRunService
    {
        public List<SubmitRunRequest> Requests { get; } = [];

        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<IReadOnlyList<RunWarningEntity>> ListRunWarningsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RunWarningEntity>>([]);
        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult<RunEntity?>(null);
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RunEntity>>([]);
        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult(false);
        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult<RunDetails?>(null);
        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult<RunWithGroundingViewModel?>(null);
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken ct = default) => Task.FromResult(new WelcomeStats(0, 0, 0, 0, 0, 0));
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken ct = default) => Task.FromResult(true);
    }
}
