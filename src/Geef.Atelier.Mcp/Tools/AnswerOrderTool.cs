using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>
/// <c>answer_order</c>: answers the questions of an order in state <c>needs_input</c>. Not registered yet: the type carries no
/// <see cref="McpServerToolTypeAttribute"/> until the MCP package builds it.
/// </summary>
public static class AnswerOrderTool
{
    public const string ToolDescription =
        "Answers the questions of an order in state needs_input and starts the writing. " +
        "Pass each answer with the id of its question, the chosen optionId and/or the person's own words, and origin: " +
        "\"user\" if the person gave the answer, \"client\" if you took it from the conversation yourself. " +
        "Use decideForMe when the person leaves a question to Atelier. " +
        "To correct an assumption, pass its id (a1, …) with the correction as freeText. " +
        "Call with skip=true to start at once with Atelier's assumptions. " +
        "Questions are asked only once, before writing; never during it.";

    [McpServerTool, Description(ToolDescription)]
    public static Task<OrderAnsweredDto> AnswerOrder(
        IOrderService orders,
        ICurrentUserService currentUser,
        [Description("The orderId from submit_order.")] string orderId,
        [Description("Answers to the questions from submit_order or get_order_status. Leave out questions nobody answered.")] IReadOnlyList<AnswerInputDto>? answers = null,
        [Description("True to start right away without answers, with Atelier's assumptions.")] bool skip = false,
        CancellationToken cancellationToken = default) =>
        throw new McpException("Not available yet.");
}
