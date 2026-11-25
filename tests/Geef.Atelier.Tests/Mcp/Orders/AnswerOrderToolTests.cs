using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Models;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Tests.Mcp.Orders;

public sealed class AnswerOrderToolTests
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeUser _alice = new("alice");

    [Fact]
    public async Task Answers_reach_the_service_as_domain_answers_and_writing_starts()
    {
        var id = AddWaiting();
        var runId = Guid.NewGuid();
        _orders.OnAnswer = r => new OrderSubmission(r.OrderId, OrderDisplayState.Queued, runId, []);

        var dto = await AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(),
        [
            new AnswerInputDto(" length ", "user", OptionId: "short"),
            new AnswerInputDto("q1", "Client", OptionId: "o2", FreeText: "  Alle Kunden in Hessen  "),
            new AnswerInputDto("a2", " USER ", FreeText: "Liefertermin: 14. März"),
            new AnswerInputDto("q2", "user", OptionId: " ", FreeText: " ", DecideForMe: true),
        ]);

        var request = Assert.Single(_orders.Answers);
        Assert.Equal(id, request.OrderId);
        Assert.Equal("alice", request.RequestingUsername);
        Assert.Equal(
            new[]
            {
                new OrderAnswer("length", "short", null, false, AnswerOrigin.User),
                new OrderAnswer("q1", "o2", "Alle Kunden in Hessen", false, AnswerOrigin.Client),
                new OrderAnswer("a2", null, "Liefertermin: 14. März", false, AnswerOrigin.User),
                new OrderAnswer("q2", null, null, true, AnswerOrigin.User),
            },
            request.Answers);
        Assert.All(request.Answers, a => Assert.Equal(RememberChoice.No, a.Remember));
        Assert.Empty(_orders.Skips);

        Assert.Equal(id.ToString(), dto.OrderId);
        Assert.Equal("queued", dto.State);
        Assert.Equal(runId.ToString(), dto.RunId);
        Assert.Equal("Writing has started. Call get_order_status with waitSeconds, then get_order_result when done is true.", dto.Message);
    }

    [Fact]
    public async Task Without_answers_and_without_skip_every_question_is_left_to_atelier()
    {
        var id = AddWaiting();

        await AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString());

        Assert.Empty(Assert.Single(_orders.Answers).Answers);
        Assert.Empty(_orders.Skips);
    }

    [Fact]
    public async Task Skip_starts_at_once_without_answers()
    {
        var id = AddWaiting();
        var runId = Guid.NewGuid();
        _orders.OnSkip = orderId => new OrderSubmission(orderId, OrderDisplayState.Queued, runId, []);

        var dto = await AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), skip: true);

        Assert.Equal((id, "alice"), Assert.Single(_orders.Skips));
        Assert.Empty(_orders.Answers);
        Assert.Equal("queued", dto.State);
        Assert.Equal(runId.ToString(), dto.RunId);
    }

    [Fact]
    public async Task Skip_with_an_empty_answer_list_is_a_plain_skip()
    {
        var id = AddWaiting();

        await AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), [], skip: true);

        Assert.Single(_orders.Skips);
        Assert.Empty(_orders.Answers);
    }

    [Fact]
    public async Task Skip_together_with_answers_is_refused()
    {
        var id = AddWaiting();

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), [new AnswerInputDto("length", "user", OptionId: "short")], skip: true));

        Assert.Equal("Either answer or skip, not both.", ex.Message);
        AssertNothingReachedTheService();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_answer_without_origin_is_refused(string? origin)
    {
        var id = AddWaiting();

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), [new AnswerInputDto("length", origin!, OptionId: "short")]));

        Assert.Contains("length has no origin", ex.Message);
        Assert.Contains("\"user\"", ex.Message);
        Assert.Contains("\"client\"", ex.Message);
        AssertNothingReachedTheService();
    }

    [Theory]
    [InlineData("assistant")]
    [InlineData("person")]
    [InlineData("users")]
    public async Task An_unknown_origin_is_refused_with_the_allowed_values(string origin)
    {
        var id = AddWaiting();

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), [new AnswerInputDto("q1", origin, OptionId: "o1")]));

        Assert.Contains($"unknown origin \"{origin}\"", ex.Message);
        Assert.Contains("\"user\"", ex.Message);
        AssertNothingReachedTheService();
    }

    [Fact]
    public async Task A_very_long_unknown_origin_is_shortened_in_the_error()
    {
        var id = AddWaiting();

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), [new AnswerInputDto("q1", new string('x', 500), OptionId: "o1")]));

        Assert.True(ex.Message.Length < 300, ex.Message);
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("Memory")]
    public async Task Memory_as_origin_is_not_allowed_over_mcp(string origin)
    {
        var id = AddWaiting();

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), [new AnswerInputDto("q1", origin, OptionId: "o1")]));

        Assert.Equal(AnswerOrderTool.MemoryOriginMessage, ex.Message);
        AssertNothingReachedTheService();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task An_answer_without_question_id_is_refused(string? questionId)
    {
        var id = AddWaiting();

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), [new AnswerInputDto(questionId!, "user", OptionId: "o1")]));

        Assert.Equal(AnswerOrderTool.MissingQuestionIdMessage, ex.Message);
        AssertNothingReachedTheService();
    }

    [Fact]
    public async Task An_answer_the_service_refuses_reaches_the_client_with_its_message()
    {
        var id = AddWaiting();
        _orders.AnswerException = new OrderAnswerException("Question q1 has no option o9. Choose one of: o1, o2.");

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), [new AnswerInputDto("q1", "user", OptionId: "o9")]));

        Assert.Equal("Question q1 has no option o9. Choose one of: o1, o2.", ex.Message);
        Assert.IsType<OrderAnswerException>(ex.InnerException);
    }

    [Fact]
    public async Task A_start_problem_of_the_service_reaches_the_client_with_its_message()
    {
        var id = AddWaiting();
        _orders.AnswerException = new OrderSubmissionException("A file of this order was deleted. Start a new order.");

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), skip: true));

        Assert.Equal("A file of this order was deleted. Start a new order.", ex.Message);
    }

    [Fact]
    public async Task A_repeated_call_is_no_error_and_passes_on_the_notice()
    {
        var id = AddWaiting();
        var runId = Guid.NewGuid();
        _orders.OnAnswer = r => new OrderSubmission(r.OrderId, OrderDisplayState.Working, runId, ["This order has already started; the answers were not changed."]);

        var dto = await AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), [new AnswerInputDto("length", "user", OptionId: "short")]);

        Assert.Equal("working", dto.State);
        Assert.Equal(runId.ToString(), dto.RunId);
        Assert.StartsWith("Writing has started.", dto.Message);
        Assert.EndsWith("This order has already started; the answers were not changed.", dto.Message);
    }

    [Theory]
    [InlineData(OrderDisplayState.Stopped, "stopped", "nothing will be written")]
    [InlineData(OrderDisplayState.Delivered, "delivered", "get_order_result")]
    [InlineData(OrderDisplayState.DeliveredWithOpenPoints, "delivered_with_open_points", "get_order_result")]
    [InlineData(OrderDisplayState.Failed, "failed", "get_order_status")]
    [InlineData(OrderDisplayState.Preparing, "preparing", "waitSeconds=25")]
    public async Task An_order_that_no_longer_waits_reports_its_state(OrderDisplayState state, string wire, string hint)
    {
        var id = AddWaiting();
        _orders.OnAnswer = r => new OrderSubmission(r.OrderId, state, null, []);

        var dto = await AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString());

        Assert.Equal(wire, dto.State);
        Assert.Contains(hint, dto.Message);
    }

    [Fact]
    public async Task An_order_still_waiting_after_the_call_has_no_run_and_repeats_the_hint()
    {
        var id = AddWaiting();
        _orders.OnAnswer = r => new OrderSubmission(r.OrderId, OrderDisplayState.NeedsInput, Guid.NewGuid(), [],
            OrderToolTestData.Clarification([OrderToolTestData.LengthQuestion]));

        var dto = await AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString());

        Assert.Equal("needs_input", dto.State);
        Assert.Null(dto.RunId);
        Assert.StartsWith("Before writing, Atelier has 1 short question.", dto.Message);
    }

    [Fact]
    public async Task A_foreign_order_is_reported_as_not_found_and_nothing_changes()
    {
        var id = Guid.NewGuid();
        _orders.Add("bob", OrderToolTestData.View(id, OrderDisplayState.NeedsInput, clarification: OrderToolTestData.Clarification()));

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            AnswerOrderTool.AnswerOrder(_orders, _alice, id.ToString(), skip: true));

        Assert.Equal("Order not found.", ex.Message);
        AssertNothingReachedTheService();
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public async Task An_invalid_id_is_reported_as_not_found(string orderId)
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => AnswerOrderTool.AnswerOrder(_orders, _alice, orderId));

        Assert.Equal("Order not found.", ex.Message);
        AssertNothingReachedTheService();
    }

    [Fact]
    public async Task An_administrator_may_answer_a_foreign_order()
    {
        var id = Guid.NewGuid();
        _orders.Add("bob", OrderToolTestData.View(id, OrderDisplayState.NeedsInput, clarification: OrderToolTestData.Clarification()));

        await AnswerOrderTool.AnswerOrder(_orders, new FakeUser("root", isAdmin: true), id.ToString(), skip: true);

        Assert.Equal((id, (string?)null), Assert.Single(_orders.Skips));
    }

    [Fact]
    public async Task Without_user_name_nothing_is_answered()
    {
        var id = AddWaiting();

        var ex = await Assert.ThrowsAsync<McpException>(() =>
            AnswerOrderTool.AnswerOrder(_orders, new FakeUser(null), id.ToString(), skip: true));

        Assert.Equal(OwnerScope.UnknownUserMessage, ex.Message);
        AssertNothingReachedTheService();
    }

    [Fact]
    public void The_tool_is_registered_for_the_mcp_server() =>
        Assert.NotNull(typeof(AnswerOrderTool).GetCustomAttributes(typeof(McpServerToolTypeAttribute), inherit: false).SingleOrDefault());

    [Fact]
    public void The_description_carries_the_agreed_wording()
    {
        Assert.Equal(
            "Answers the questions of an order in state needs_input and starts the writing. " +
            "Pass each answer with the id of its question, the chosen optionId and/or the person's own words, and origin: " +
            "\"user\" if the person gave the answer, \"client\" if you took it from the conversation yourself. " +
            "Use decideForMe when the person leaves a question to Atelier. " +
            "To correct an assumption, pass its id (a1, …) with the correction as freeText. " +
            "If the order shows revisionFile, Atelier will improve that file; to write something new instead pass questionId \"revision\" with optionId \"no\". " +
            "Call with skip=true, or without answers, to start at once with Atelier's assumptions. " +
            "Questions are asked only once, before writing; never during it.",
            AnswerOrderTool.ToolDescription);
    }

    private Guid AddWaiting()
    {
        var id = Guid.NewGuid();
        _orders.Add("alice", OrderToolTestData.View(id, OrderDisplayState.NeedsInput, clarification: OrderToolTestData.Clarification()));
        return id;
    }

    private void AssertNothingReachedTheService()
    {
        Assert.Empty(_orders.Answers);
        Assert.Empty(_orders.Skips);
    }
}
