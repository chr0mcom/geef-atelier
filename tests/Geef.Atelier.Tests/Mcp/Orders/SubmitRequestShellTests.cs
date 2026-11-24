using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary><c>submit_request</c> as a shell over the order service: same answer (run id, Pending) plus the order id.</summary>
public sealed class SubmitRequestShellTests
{
    private const string Briefing = "Write three sentences that explain why the office closes early on Fridays.";

    private readonly FakeOrderService _orders = new();
    private readonly FakeSourceIntake _intake = new();
    private readonly FakeUser _admin = new("admin", isAdmin: true);

    [Fact]
    public async Task Without_template_it_creates_a_direct_order_without_questions_and_returns_run_and_order()
    {
        var orderId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        _orders.OnSubmit = _ => new OrderSubmission(orderId, OrderDisplayState.Queued, runId, []);

        var dto = await SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing);

        var submitted = Assert.Single(_orders.Submitted);
        Assert.Equal(Briefing, submitted.RequestText);
        Assert.Equal("admin", submitted.CreatedByUser);
        Assert.Equal(OrderEntry.Mcp, submitted.Entry);
        Assert.Equal(OrderMode.Direct, submitted.Mode);
        Assert.False(submitted.AllowQuestions);
        Assert.Null(submitted.CrewTemplateName);
        Assert.Null(submitted.CustomCrew);

        Assert.Equal(runId.ToString(), dto.RunId);
        Assert.Equal("Pending", dto.Status);
        Assert.Null(dto.ErrorMessage);
        Assert.Equal(orderId.ToString(), dto.OrderId);
    }

    [Fact]
    public async Task The_default_mode_follows_the_switch()
    {
        await SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(legacyMode: OrderMode.Careful), Briefing);

        var submitted = Assert.Single(_orders.Submitted);
        Assert.Equal(OrderMode.Careful, submitted.Mode);
        Assert.Null(submitted.CrewTemplateName);
    }

    [Fact]
    public async Task With_template_it_creates_a_careful_order_with_that_template()
    {
        await SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing, crewTemplate: "klassik");

        var submitted = Assert.Single(_orders.Submitted);
        Assert.Equal(OrderMode.Careful, submitted.Mode);
        Assert.Equal("klassik", submitted.CrewTemplateName);
        Assert.Null(submitted.CustomCrew);
    }

    [Fact]
    public async Task A_valid_custom_crew_wins_over_the_template()
    {
        const string json = """
            {
              "executorProfileName": "default-executor",
              "reviewerProfileNames": ["briefing-fidelity"],
              "evaluationStrategy": 0,
              "convergenceOverride": null
            }
            """;

        await SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing, crewTemplate: "klassik", customCrew: json);

        var submitted = Assert.Single(_orders.Submitted);
        Assert.Equal(OrderMode.Careful, submitted.Mode);
        Assert.Null(submitted.CrewTemplateName);
        Assert.Equal("default-executor", submitted.CustomCrew!.ExecutorProfileName);
        Assert.Equal(["briefing-fidelity"], submitted.CustomCrew.ReviewerProfileNames);
    }

    [Fact]
    public async Task A_custom_crew_without_reviewer_list_gets_an_empty_list()
    {
        await SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing,
            customCrew: """{"executorProfileName":"default-executor"}""");

        Assert.Empty(Assert.Single(_orders.Submitted).CustomCrew!.ReviewerProfileNames);
    }

    [Theory]
    [InlineData("not-json{{")]
    [InlineData("""{"executorProfileName": "default-executor",""")]
    public async Task Broken_custom_crew_json_is_reported_instead_of_ignored(string customCrew)
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing, crewTemplate: "klassik", customCrew: customCrew));

        Assert.Contains("customCrew is not valid JSON", ex.Message);
        Assert.Empty(_orders.Submitted);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"reviewerProfileNames":["briefing-fidelity"]}""")]
    public async Task A_custom_crew_without_executor_is_rejected(string customCrew)
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing, customCrew: customCrew));

        Assert.Contains("executorProfileName", ex.Message);
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task Invalid_config_json_is_rejected_and_valid_config_json_is_accepted()
    {
        await Assert.ThrowsAsync<McpException>(() =>
            SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing, configJson: "{broken"));

        await SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing, configJson: """{"tone":"formal"}""");
        Assert.Single(_orders.Submitted);
    }

    [Fact]
    public async Task Attachments_go_through_the_intake_and_into_the_order()
    {
        var json = Attachments.Json(
            ("a.txt", "text/plain", "The first file has enough text."u8.ToArray()),
            ("b.pdf", "application/pdf", Attachments.MinimalPdf));

        await SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing, attachments: json);

        Assert.Equal(["a.txt", "b.pdf"], _intake.Intakes.Select(i => i.FileName));
        Assert.All(_intake.Intakes, i => Assert.Equal(SourceOrigin.McpAttachment, i.Origin));
        Assert.All(_intake.Intakes, i => Assert.Equal("admin", i.Owner));
        Assert.Equal("The first file has enough text."u8.ToArray(), _intake.Intakes[0].Content);
        Assert.Equal(2, Assert.Single(_orders.Submitted).Sources!.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    public async Task Without_attachments_nothing_is_read(string? attachments)
    {
        await SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing, attachments: attachments);

        Assert.Empty(_intake.Intakes);
        Assert.Empty(Assert.Single(_orders.Submitted).Sources!);
    }

    [Fact]
    public async Task An_unusable_attachment_is_reported_with_its_reason()
    {
        _intake.UnusableReasons["scan.pdf"] = "The PDF is encrypted";
        var json = Attachments.Json(("scan.pdf", "application/pdf", Attachments.MinimalPdf));

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing, attachments: json));

        Assert.Contains("'scan.pdf' cannot be used: The PDF is encrypted.", ex.Message);
        Assert.Empty(_orders.Submitted);
    }

    [Theory]
    [InlineData("not valid json{{", "filename, contentType and contentBase64")]
    [InlineData("""[{"filename":"doc.md","contentType":"text/plain","contentBase64":"!!!not-base64!!!"}]""", "doc.md")]
    [InlineData("""[{"filename":"doc.doc","contentType":"application/msword","contentBase64":"YmluYXJ5"}]""", "application/msword")]
    [InlineData("""[{"filename":"data.bin","contentType":"application/octet-stream","contentBase64":"YmluYXJ5IGRhdGE="}]""", "application/octet-stream")]
    public async Task Malformed_attachments_are_reported_with_a_readable_message(string attachments, string expected)
    {
        var ex = await Assert.ThrowsAsync<McpException>(() =>
            SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing, attachments: attachments));

        Assert.Contains(expected, ex.Message);
        Assert.Empty(_intake.Intakes);
    }

    [Fact]
    public async Task A_submission_error_is_reported_in_plain_words()
    {
        _orders.SubmitException = new OrderSubmissionException("You can attach at most 10 files.");

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing));

        Assert.Equal("You can attach at most 10 files.", ex.Message);
    }

    [Fact]
    public async Task An_order_without_run_is_reported_instead_of_returning_an_empty_run_id()
    {
        var orderId = Guid.NewGuid();
        _orders.OnSubmit = _ => new OrderSubmission(orderId, OrderDisplayState.NeedsInput, null, []);

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), Briefing));

        Assert.Contains(orderId.ToString(), ex.Message);
        Assert.Contains("get_order_status", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task An_empty_briefing_is_rejected(string briefing)
    {
        await Assert.ThrowsAsync<McpException>(() =>
            SubmitRequestTool.SubmitRequest(_orders, _intake, _admin, OrderToolTestData.Options(), briefing));

        Assert.Empty(_orders.Submitted);
    }
}
