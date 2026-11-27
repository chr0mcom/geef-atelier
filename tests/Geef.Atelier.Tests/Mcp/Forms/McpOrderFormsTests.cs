using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Forms;
using Geef.Atelier.Mcp.Models;
using Geef.Atelier.Tests.Mcp.Orders;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Geef.Atelier.Tests.Mcp.Forms;

/// <summary>
/// The way back of the form of <c>submit_order</c> (D-088): answers reach only the order the state names, only for its user, client and call; a
/// closed, declined, expired or unusable form gives <c>needs_input</c> as before, and nothing ever creates a second order.
/// </summary>
public sealed class McpOrderFormsTests
{
    private const string Arguments = "args-1";

    private readonly FakeOrderService _orders = new();
    private readonly OrderFormStatesTests.SteppedTime _time = new(new DateTimeOffset(2026, 11, 27, 9, 0, 0, TimeSpan.Zero));
    private readonly EphemeralDataProtectionProvider _keys = new();
    private readonly McpOptions _options = new() { Elicitation = McpElicitationMode.On, ElicitationLifetime = TimeSpan.FromMinutes(30) };
    private readonly Guid _orderId = Guid.NewGuid();
    private static readonly IReadOnlyList<OrderSourceDto> Sources = [new("angebot.pdf", 1, 900, "good", [])];

    public McpOrderFormsTests() =>
        _orders.Add("alice", OrderToolTestData.View(_orderId, OrderDisplayState.NeedsInput, clarification: OrderToolTestData.Clarification()));

    private OrderFormStates States => new(_keys, _time, Options.Create(_options));

    private McpOrderForms Forms => new(_orders, States, Options.Create(_options), NullLogger<McpOrderForms>.Instance);

    private string State(string username = "alice", string? client = "client-1", string arguments = Arguments, Guid? orderId = null) =>
        States.Protect(States.Create(orderId ?? _orderId, username, client, arguments, ["length", "q1"], Sources, ["Eine Datei war doppelt."]));

    private static CallToolRequestParams Parameters(string? state, ElicitResult? result) => new()
    {
        Name = "submit_order",
        RequestState = state,
        InputResponses = result is null
            ? null
            : new Dictionary<string, InputResponse> { [OrderQuestionForm.InputKey] = JsonSerializer.Deserialize<InputResponse>(JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions), McpJsonUtilities.DefaultOptions)! },
    };

    private static OrderFormCall Call(CallToolRequestParams parameters, string username = "alice", string? client = "client-1", string arguments = Arguments) =>
        new(null, parameters, username, client, arguments);

    private static ElicitResult Accept(params (string Key, string Value)[] values) =>
        new() { Action = "accept", Content = values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value)) };

    [Fact]
    public async Task Without_a_state_the_call_goes_on_as_usual()
    {
        Assert.Null(await Forms.ResumeAsync(Call(Parameters(null, null)), CancellationToken.None));
        Assert.Empty(_orders.Answers);
    }

    [Fact]
    public async Task Sent_answers_answer_the_named_order_with_the_form_origin_and_bring_back_the_receipts_of_the_first_call()
    {
        _orders.OnAnswer = r => new OrderSubmission(r.OrderId, OrderDisplayState.Queued, Guid.NewGuid(), ["Eine Notiz."]);

        var dto = await Forms.ResumeAsync(Call(Parameters(State(), Accept(("length", "short"), ("q1", "o2")))), CancellationToken.None);

        var answered = Assert.Single(_orders.Answers);
        Assert.Equal((_orderId, "alice"), (answered.OrderId, answered.RequestingUsername));
        Assert.Equal(
            [new OrderAnswer("length", "short", null, false, AnswerOrigin.Form), new OrderAnswer("q1", "o2", null, false, AnswerOrigin.Form)],
            answered.Answers);
        Assert.Equal(_orderId.ToString(), dto!.OrderId);
        Assert.Equal("queued", dto.State);
        Assert.Equal("angebot.pdf", Assert.Single(dto.Sources).FileName);
        Assert.Contains("Eine Datei war doppelt.", dto.Message, StringComparison.Ordinal);
        Assert.Contains("Eine Notiz.", dto.Message, StringComparison.Ordinal);
        Assert.StartsWith(McpOrderForms.AnsweredLead, dto.Message, StringComparison.Ordinal);
        Assert.Equal(
        [
            new GivenAnswerDto("length", "Wie lang soll der Text ungefähr werden?", "Kurz (etwa 150 Wörter)", false, "form"),
            new GivenAnswerDto("q1", "Wer bekommt die Mail?", "Alle Kunden", false, "form"),
        ], dto.GivenAnswers);
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task You_decide_and_empty_questions_show_as_left_to_atelier_in_the_given_answers()
    {
        var dto = await Forms.ResumeAsync(Call(Parameters(State(), Accept(("length", "decide_for_me")))), CancellationToken.None);

        Assert.Equal(
        [
            new GivenAnswerDto("length", "Wie lang soll der Text ungefähr werden?", null, true, "form"),
            new GivenAnswerDto("q1", "Wer bekommt die Mail?", null, true, "form"),
        ], dto!.GivenAnswers);
    }

    [Fact]
    public async Task Without_answers_the_given_answers_stay_empty()
    {
        var dto = await Forms.ResumeAsync(Call(Parameters(State(), new ElicitResult { Action = "decline" })), CancellationToken.None);

        Assert.Null(dto!.GivenAnswers);
    }

    [Theory]
    [InlineData("decline", McpOrderForms.DeclinedLead)]
    [InlineData("cancel", McpOrderForms.CancelledLead)]
    public async Task A_declined_or_closed_form_gives_needs_input_as_before(string action, string lead)
    {
        var dto = await Forms.ResumeAsync(Call(Parameters(State(), new ElicitResult { Action = action })), CancellationToken.None);

        Assert.Empty(_orders.Answers);
        Assert.Equal("needs_input", dto!.State);
        Assert.Equal(2, dto.Questions!.Count);
        Assert.StartsWith(lead + " " + OrderDtoMappings.NeedsInputMessage(2), dto.Message, StringComparison.Ordinal);
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task No_answers_at_all_count_as_a_closed_form()
    {
        var dto = await Forms.ResumeAsync(Call(Parameters(State(), null)), CancellationToken.None);

        Assert.StartsWith(McpOrderForms.CancelledLead, dto!.Message, StringComparison.Ordinal);
        Assert.Empty(_orders.Answers);
    }

    [Fact]
    public async Task A_form_sent_without_a_single_choice_counts_as_closed_and_starts_nothing()
    {
        var dto = await Forms.ResumeAsync(Call(Parameters(State(), Accept())), CancellationToken.None);

        Assert.StartsWith(McpOrderForms.CancelledLead, dto!.Message, StringComparison.Ordinal);
        Assert.Equal("needs_input", dto.State);
        Assert.Empty(_orders.Answers);
    }

    [Fact]
    public async Task Answers_that_do_not_fit_the_form_give_needs_input()
    {
        var dto = await Forms.ResumeAsync(Call(Parameters(State(), Accept(("q1", "o9")))), CancellationToken.None);

        Assert.StartsWith(McpOrderForms.InvalidLead, dto!.Message, StringComparison.Ordinal);
        Assert.Empty(_orders.Answers);
    }

    [Fact]
    public async Task An_expired_state_gives_needs_input_and_answers_nothing()
    {
        var state = State();
        _time.Advance(TimeSpan.FromMinutes(31));

        var dto = await Forms.ResumeAsync(Call(Parameters(state, Accept(("length", "short")))), CancellationToken.None);

        Assert.StartsWith(McpOrderForms.ExpiredLead, dto!.Message, StringComparison.Ordinal);
        Assert.Equal("needs_input", dto.State);
        Assert.Empty(_orders.Answers);
    }

    [Theory]
    [InlineData("bob", "client-1", Arguments)]
    [InlineData("alice", "client-2", Arguments)]
    [InlineData("alice", null, Arguments)]
    [InlineData("alice", "client-1", "args-2")]
    public async Task A_state_of_another_user_client_or_call_answers_nothing_and_creates_nothing(string username, string? client, string arguments)
    {
        var call = Call(Parameters(State(), Accept(("length", "short"))), username, client, arguments);

        var error = await Assert.ThrowsAsync<McpException>(() => Forms.ResumeAsync(call, CancellationToken.None));

        Assert.Equal(McpOrderForms.NotMatchedMessage, error.Message);
        Assert.Empty(_orders.Answers);
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task A_state_for_an_order_of_someone_else_answers_nothing()
    {
        var foreign = Guid.NewGuid();
        _orders.Add("bob", OrderToolTestData.View(foreign, OrderDisplayState.NeedsInput, clarification: OrderToolTestData.Clarification()));

        var error = await Assert.ThrowsAsync<McpException>(() =>
            Forms.ResumeAsync(Call(Parameters(State(orderId: foreign), Accept(("length", "short")))), CancellationToken.None));

        Assert.Equal(McpOrderForms.NotMatchedMessage, error.Message);
        Assert.Empty(_orders.Answers);
    }

    [Fact]
    public async Task A_forged_state_fails_while_forms_are_on_and_is_ignored_while_they_are_off()
    {
        var forged = Call(Parameters("order-123", Accept(("length", "short"))));

        var error = await Assert.ThrowsAsync<McpException>(() => Forms.ResumeAsync(forged, CancellationToken.None));
        Assert.Equal(McpOrderForms.NotMatchedMessage, error.Message);

        _options.Elicitation = McpElicitationMode.Off;
        Assert.Null(await Forms.ResumeAsync(forged, CancellationToken.None));
        Assert.Empty(_orders.Answers);
    }

    [Fact]
    public async Task A_real_state_that_comes_back_while_forms_are_off_answers_nothing_and_creates_no_second_order()
    {
        var state = State();
        _options.Elicitation = McpElicitationMode.Off;

        var dto = await Forms.ResumeAsync(Call(Parameters(state, Accept(("length", "short")))), CancellationToken.None);

        Assert.Equal(_orderId.ToString(), dto!.OrderId);
        Assert.StartsWith(McpOrderForms.SwitchedOffLead, dto.Message, StringComparison.Ordinal);
        Assert.Empty(_orders.Answers);
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task A_second_call_with_the_same_state_after_the_answers_gives_the_current_state_without_answering_again()
    {
        var state = State();
        _orders.Add("alice", OrderToolTestData.View(_orderId, OrderDisplayState.Working, runId: Guid.NewGuid(), clarification: OrderToolTestData.Clarification()));

        var dto = await Forms.ResumeAsync(Call(Parameters(state, Accept(("length", "short")))), CancellationToken.None);

        Assert.Equal("working", dto!.State);
        Assert.Null(dto.Questions);
        Assert.Empty(_orders.Answers);
    }

    [Fact]
    public async Task Changed_questions_give_needs_input_with_the_questions_of_now()
    {
        var state = States.Protect(States.Create(_orderId, "alice", "client-1", Arguments, ["q7"], Sources, []));

        var dto = await Forms.ResumeAsync(Call(Parameters(state, Accept(("q7", "o1")))), CancellationToken.None);

        Assert.StartsWith(McpOrderForms.InvalidLead, dto!.Message, StringComparison.Ordinal);
        Assert.Equal(["length", "q1"], dto.Questions!.Select(q => q.Id));
        Assert.Empty(_orders.Answers);
    }

    [Fact]
    public async Task An_answer_the_order_service_refuses_gives_needs_input()
    {
        _orders.AnswerException = new OrderAnswerException("The answer to \"length\" is too long.");

        var dto = await Forms.ResumeAsync(Call(Parameters(State(), Accept(("length", "short")))), CancellationToken.None);

        Assert.StartsWith(McpOrderForms.InvalidLead, dto!.Message, StringComparison.Ordinal);
        Assert.Equal("needs_input", dto.State);
    }

    [Theory]
    [InlineData("form")]
    [InlineData("FORM")]
    public void A_client_can_never_claim_the_form_origin_in_answer_order(string origin)
    {
        var error = Assert.Throws<McpException>(() => Geef.Atelier.Mcp.Tools.AnswerOrderTool.ToOrderAnswer(new AnswerInputDto("q1", origin, "o1")));

        Assert.Contains("unknown origin", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_server_or_while_off_no_form_is_asked()
    {
        var dto = new OrderSubmittedDto(_orderId.ToString(), "needs_input", null, "m", Sources);

        Forms.AskWhenPossible(Call(Parameters(null, null)), dto, OrderToolTestData.Clarification(), []);
        _options.Elicitation = McpElicitationMode.Off;
        Forms.AskWhenPossible(Call(Parameters(null, null)), dto, OrderToolTestData.Clarification(), []);
    }
}
