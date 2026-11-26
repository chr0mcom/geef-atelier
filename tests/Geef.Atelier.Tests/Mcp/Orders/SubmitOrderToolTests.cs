using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Mcp.Models;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

public sealed class SubmitOrderToolTests
{
    private const string Request = "Schreib eine kurze, freundliche Mail an den Kunden: Die Lieferung verzögert sich um zwei Wochen.";

    private readonly FakeOrderService _orders = new();
    private readonly FakeSourceIntake _intake = new();
    private readonly FakeUser _alice = new("alice");
    private readonly FakeUploadTickets _uploads = new(enabled: false);
    private readonly FakeRefusalLog _refusals = new();

    [Fact]
    public async Task Request_is_passed_verbatim_as_a_direct_mcp_order_that_may_ask_questions()
    {
        var orderId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        _orders.OnSubmit = _ => new OrderSubmission(orderId, OrderDisplayState.Queued, runId, []);

        var dto = await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request);

        var submitted = Assert.Single(_orders.Submitted);
        Assert.Equal(Request, submitted.RequestText);
        Assert.Equal("alice", submitted.CreatedByUser);
        Assert.Equal(OrderEntry.Mcp, submitted.Entry);
        Assert.Equal(OrderMode.Direct, submitted.Mode);
        Assert.True(submitted.AllowQuestions);
        Assert.Empty(submitted.Sources!);
        Assert.Null(submitted.CrewTemplateName);

        Assert.Equal(orderId.ToString(), dto.OrderId);
        Assert.Equal("queued", dto.State);
        Assert.Equal(runId.ToString(), dto.RunId);
        Assert.Contains("get_order_status", dto.Message);
        Assert.Contains("waitSeconds", dto.Message);
        Assert.Empty(dto.Sources);
        Assert.Empty(_intake.Intakes);
        Assert.Empty(_orders.Waits);
        Assert.Null(dto.Questions);
        Assert.Null(dto.Understanding);
    }

    [Fact]
    public async Task Extra_careful_submits_a_careful_order()
    {
        await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request, extraCareful: true);

        Assert.Equal(OrderMode.Careful, Assert.Single(_orders.Submitted).Mode);
    }

    [Fact]
    public async Task Notices_of_the_service_are_appended_to_the_message()
    {
        _orders.OnSubmit = _ => new OrderSubmission(Guid.NewGuid(), OrderDisplayState.Queued, Guid.NewGuid(), ["One file is very long and is read with a map."]);

        var dto = await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request);

        Assert.EndsWith("One file is very long and is read with a map.", dto.Message);
    }

    [Fact]
    public async Task Attachments_go_through_the_intake_as_mcp_sources_of_the_caller_and_into_the_order()
    {
        var json = Attachments.Json(
            ("offer.pdf", "application/pdf", Attachments.MinimalPdf),
            ("notes.md", "text/markdown", "# Notes\n\nDelivery in calendar week 12."u8.ToArray()));

        var dto = await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request, attachments: json);

        Assert.Equal(2, _intake.Intakes.Count);
        Assert.All(_intake.Intakes, i => Assert.Equal("alice", i.Owner));
        Assert.All(_intake.Intakes, i => Assert.Equal(SourceOrigin.McpAttachment, i.Origin));
        Assert.Equal(Attachments.MinimalPdf, _intake.Intakes[0].Content);
        Assert.Equal(Attachments.MinimalPdf.Length, _intake.Intakes[0].Length);
        Assert.Equal("application/pdf", _intake.Intakes[0].ContentType);

        var sources = Assert.Single(_orders.Submitted).Sources!;
        Assert.Equal(2, sources.Count);
        Assert.All(sources, s => Assert.Equal(SourceRole.Facts, s.Role));
        Assert.All(sources, s => Assert.Null(s.PageSelection));

        Assert.Equal(2, dto.Sources.Count);
        Assert.Equal("offer.pdf", dto.Sources[0].FileName);
        Assert.Equal(4, dto.Sources[0].Pages);
        Assert.Equal("partial", dto.Sources[0].Quality);
        Assert.Equal(["Page 3 has no text."], dto.Sources[0].Warnings);
        Assert.Equal("good", dto.Sources[1].Quality);
        Assert.Null(dto.Sources[1].Pages);
    }

    [Fact]
    public async Task An_unusable_file_stops_the_order_with_its_reason_and_releases_new_files_only()
    {
        _intake.UnusableReasons["scan.pdf"] = "The PDF contains no readable text (probably a scan)";
        _intake.Duplicates.Add("known.md");
        var json = Attachments.Json(
            ("new.md", "text/markdown", "# Notes\n\nDelivery in calendar week 12."u8.ToArray()),
            ("known.md", "text/markdown", "# Older notes\n\nThe customer is in Sampletown."u8.ToArray()),
            ("scan.pdf", "application/pdf", Attachments.MinimalPdf));

        var ex = await Assert.ThrowsAnyAsync<McpException>(() =>
            SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request, attachments: json));

        Assert.Contains("'scan.pdf' cannot be used: The PDF contains no readable text (probably a scan).", ex.Message);
        Assert.Contains("Nothing was started", ex.Message);
        Assert.Contains("Atelier web app", ex.Message);
        Assert.Empty(_orders.Submitted);
        var deleted = Assert.Single(_intake.Deletes);
        Assert.Equal("alice", deleted.RequestingUsername);
        Assert.Equal(3, _intake.Intakes.Count);
    }

    [Fact]
    public async Task A_placeholder_attachment_is_rejected_before_any_file_is_stored()
    {
        var json = Attachments.Text("vertrag.md", "Siehe angehängte Vollversion im Briefing-Kontext", "text/markdown");

        var ex = await Assert.ThrowsAnyAsync<McpException>(() =>
            SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request, attachments: json));

        Assert.Contains("vertrag.md", ex.Message);
        Assert.Contains("placeholder", ex.Message);
        Assert.Empty(_intake.Intakes);
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task A_submission_error_is_reported_in_plain_words_and_releases_the_new_files()
    {
        _orders.SubmitException = new OrderSubmissionException(
            "Your files are too long to use completely (950,000 characters, the limit is 900,000). Choose pages for: big.pdf");
        var json = Attachments.Json(("big.pdf", "application/pdf", Attachments.MinimalPdf));

        var ex = await Assert.ThrowsAnyAsync<McpException>(() =>
            SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request, attachments: json));

        Assert.StartsWith("Your files are too long to use completely", ex.Message);
        Assert.Single(_intake.Deletes);
    }

    [Fact]
    public async Task An_order_that_needs_answers_is_reported_as_needs_input_without_run()
    {
        var orderId = Guid.NewGuid();
        _orders.OnSubmit = _ => new OrderSubmission(orderId, OrderDisplayState.NeedsInput, null, []);

        var dto = await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request);

        Assert.Equal("needs_input", dto.State);
        Assert.Null(dto.RunId);
        Assert.Equal(orderId.ToString(), dto.OrderId);
        Assert.Contains("answers", dto.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_request_is_rejected_before_files_are_read(string request)
    {
        var json = Attachments.Json(("offer.pdf", "application/pdf", Attachments.MinimalPdf));

        var ex = await Assert.ThrowsAnyAsync<McpException>(() =>
            SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), request, attachments: json));

        Assert.Contains("request is empty", ex.Message);
        Assert.Empty(_intake.Intakes);
    }

    [Fact]
    public async Task Without_user_no_order_is_created()
    {
        await Assert.ThrowsAnyAsync<McpException>(() =>
            SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, new FakeUser(null), OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request));

        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public void Description_tells_the_client_to_pass_the_words_of_the_person_and_to_wait_with_the_status_tool()
    {
        Assert.Contains("in their own words; do not rewrite or expand it", SubmitOrderTool.ToolDescription);
        Assert.Contains("get_order_status with waitSeconds", SubmitOrderTool.ToolDescription);
        Assert.Contains("Waits a short while for Atelier to read the request.", SubmitOrderTool.ToolDescription);
        Assert.DoesNotContain("30 seconds", SubmitOrderTool.ToolDescription);
        Assert.Contains("state needs_input and up to three short questions with answer options: ask the person, then call answer_order.", SubmitOrderTool.ToolDescription);
    }

    [Fact]
    public async Task While_the_request_is_read_the_tool_waits_and_answers_with_the_questions()
    {
        var orderId = Guid.NewGuid();
        _orders.OnSubmit = _ => new OrderSubmission(orderId, OrderDisplayState.Preparing, null, ["One file is very long and is read with a map."]);
        _orders.ViewAfterWait = OrderToolTestData.View(orderId, OrderDisplayState.NeedsInput, runId: null, clarification: OrderToolTestData.Clarification());
        var json = Attachments.Json(("notes.md", "text/markdown", "# Notes\n\nDelivery in calendar week 12."u8.ToArray()));

        var dto = await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request, attachments: json);

        var wait = Assert.Single(_orders.Waits);
        Assert.Equal(orderId, wait.OrderId);
        Assert.Equal("alice", wait.RequestingUsername);
        Assert.Equal(OrderDisplayState.Preparing, wait.KnownState);
        Assert.Equal(TimeSpan.FromSeconds(30), wait.MaxWait);

        Assert.Equal("needs_input", dto.State);
        Assert.Null(dto.RunId);
        Assert.Equal("Eine kurze Mail an einen Kunden über eine Lieferverzögerung.", dto.Understanding);
        Assert.Equal("de", dto.Language);
        Assert.Equal(
            [new AssumptionDto("a1", "Freundlicher, sachlicher Ton", false), new AssumptionDto("a2", "Neuer Liefertermin als Platzhalter", true)],
            dto.Assumptions!);

        Assert.Equal(2, dto.Questions!.Count);
        var length = dto.Questions[0];
        Assert.Equal("length", length.Id);
        Assert.Equal("length", length.Topic);
        Assert.Equal("Wie lang soll der Text ungefähr werden?", length.Text);
        Assert.Equal(["short", "medium", "long"], length.Options.Select(o => o.Id));
        Assert.Equal("Kurz (etwa 150 Wörter)", length.Options[0].Label);
        Assert.Null(length.DefaultOptionId);
        Assert.True(length.AllowFreeText);
        var audience = dto.Questions[1];
        Assert.Equal("q1", audience.Id);
        Assert.Equal("audience", audience.Topic);
        Assert.Equal("o1", audience.DefaultOptionId);
        Assert.False(audience.AllowFreeText);

        Assert.StartsWith(
            "Before writing, Atelier has 2 short questions. Ask the person in your own words, show the answer options, and say that skipping is fine. " +
            "Then call answer_order with their answers (origin \"user\"), or with skip=true.",
            dto.Message);
        Assert.EndsWith("One file is very long and is read with a map.", dto.Message);
        Assert.Equal("notes.md", Assert.Single(dto.Sources).FileName);
    }

    [Fact]
    public async Task One_question_is_announced_in_the_singular()
    {
        var orderId = Guid.NewGuid();
        _orders.OnSubmit = _ => new OrderSubmission(orderId, OrderDisplayState.Preparing, null, []);
        _orders.ViewAfterWait = OrderToolTestData.View(orderId, OrderDisplayState.NeedsInput, runId: null,
            clarification: OrderToolTestData.Clarification([OrderToolTestData.LengthQuestion]));

        var dto = await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request);

        Assert.StartsWith("Before writing, Atelier has 1 short question. Ask the person", dto.Message);
    }

    [Fact]
    public async Task Without_questions_the_order_is_already_written_and_the_answer_has_the_run_understanding_and_assumptions()
    {
        var orderId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        _orders.OnSubmit = _ => new OrderSubmission(orderId, OrderDisplayState.Preparing, null, []);
        _orders.ViewAfterWait = OrderToolTestData.View(orderId, OrderDisplayState.Queued, runId: runId,
            clarification: OrderToolTestData.Clarification([], IntakeOutcome.Clarified));

        var dto = await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request);

        Assert.Equal("queued", dto.State);
        Assert.Equal(runId.ToString(), dto.RunId);
        Assert.Null(dto.Questions);
        Assert.Equal("Eine kurze Mail an einen Kunden über eine Lieferverzögerung.", dto.Understanding);
        Assert.Equal(2, dto.Assumptions!.Count);
        Assert.Contains("get_order_status with waitSeconds", dto.Message);
    }

    [Fact]
    public async Task When_reading_takes_longer_the_answer_is_preparing_and_points_to_the_status_tool()
    {
        var orderId = Guid.NewGuid();
        _orders.OnSubmit = _ => new OrderSubmission(orderId, OrderDisplayState.Preparing, null, []);
        _orders.ViewAfterWait = OrderToolTestData.View(orderId, OrderDisplayState.Preparing, step: "Reading your request", runId: null);

        var dto = await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request);

        Assert.Equal("preparing", dto.State);
        Assert.Null(dto.RunId);
        Assert.Null(dto.Questions);
        Assert.Equal("Still reading the request. Call get_order_status with waitSeconds=25.", dto.Message);
    }

    [Theory]
    [InlineData(30, 50, 30)]
    [InlineData(80, 50, 50)]
    [InlineData(30, 10, 10)]
    public async Task The_wait_is_the_configured_submit_wait_capped_by_the_longest_wait(int submitWait, int maxWait, int expected)
    {
        var orderId = Guid.NewGuid();
        _orders.OnSubmit = _ => new OrderSubmission(orderId, OrderDisplayState.Preparing, null, []);
        _orders.ViewAfterWait = OrderToolTestData.View(orderId, OrderDisplayState.Preparing, runId: null);

        await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(maxWait), OrderToolTestData.Intake(submitWait), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request);

        Assert.Equal(TimeSpan.FromSeconds(expected), Assert.Single(_orders.Waits).MaxWait);
    }

    [Fact]
    public async Task A_submit_wait_of_zero_answers_at_once_with_preparing()
    {
        _orders.OnSubmit = _ => new OrderSubmission(Guid.NewGuid(), OrderDisplayState.Preparing, null, []);

        var dto = await SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(0), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request);

        Assert.Empty(_orders.Waits);
        Assert.Equal("preparing", dto.State);
        Assert.Equal("Still reading the request. Call get_order_status with waitSeconds=25.", dto.Message);
    }

    [Fact]
    public async Task An_order_deleted_while_it_is_read_is_reported_as_not_found()
    {
        _orders.OnSubmit = _ => new OrderSubmission(Guid.NewGuid(), OrderDisplayState.Preparing, null, []);
        _orders.DeleteDuringWait = true;

        var ex = await Assert.ThrowsAnyAsync<McpException>(() =>
            SubmitOrderTool.SubmitOrder(_orders, _intake, _uploads, _refusals, _alice, OrderToolTestData.Options(), OrderToolTestData.Intake(), OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request));

        Assert.Equal("Order not found.", ex.Message);
    }
}
