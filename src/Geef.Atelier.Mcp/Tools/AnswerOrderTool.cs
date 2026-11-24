using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary><c>answer_order</c>: answers the questions of an order in state <c>needs_input</c>, or skips them, and starts the writing.</summary>
[McpServerToolType]
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

    public const string AnswerOrSkipMessage = "Either answer or skip, not both.";
    public const string MissingQuestionIdMessage = "Each answer needs the questionId of a question (q1, q2, q3, length) or of an assumption (a1, a2, …).";

    public const string MemoryOriginMessage =
        "origin \"memory\" cannot be used here. Use \"user\" when the person gave the answer, \"client\" when you took it from the conversation yourself.";

    [McpServerTool, Description(ToolDescription)]
    public static async Task<OrderAnsweredDto> AnswerOrder(
        IOrderService orders,
        ICurrentUserService currentUser,
        [Description("The orderId from submit_order.")] string orderId,
        [Description("Answers to the questions from submit_order or get_order_status. Leave out questions nobody answered.")] IReadOnlyList<AnswerInputDto>? answers = null,
        [Description("True to start right away without answers, with Atelier's assumptions.")] bool skip = false,
        CancellationToken cancellationToken = default)
    {
        var id = McpOrderAccess.ParseOrderId(orderId);
        var requestingUsername = currentUser.RequestingUsername();
        var given = answers ?? [];
        if (skip && given.Count > 0)
            throw new McpException(AnswerOrSkipMessage);

        var mapped = given.Select(ToOrderAnswer).ToList();

        _ = await orders.GetAsync(id, requestingUsername, cancellationToken)
            ?? throw new McpException(McpOrderAccess.OrderNotFound);

        try
        {
            var submission = skip
                ? await orders.SkipClarificationAsync(id, requestingUsername, cancellationToken)
                : await orders.AnswerAsync(new AnswerOrderRequest(id, requestingUsername, mapped), cancellationToken);
            return submission.ToAnsweredDto();
        }
        catch (OrderAnswerException ex)
        {
            throw new McpException(ex.Message, ex);
        }
        catch (OrderSubmissionException ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }

    /// <summary>Turns one answer of the wire into the domain answer; the service checks ids, options and lengths.</summary>
    public static OrderAnswer ToOrderAnswer(AnswerInputDto answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (string.IsNullOrWhiteSpace(answer.QuestionId))
            throw new McpException(MissingQuestionIdMessage);

        var questionId = answer.QuestionId.Trim();
        var origin = OrderWireNames.ParseAnswerOrigin(answer.Origin) switch
        {
            AnswerOrigin.User => AnswerOrigin.User,
            AnswerOrigin.Client => AnswerOrigin.Client,
            AnswerOrigin.Memory => throw new McpException(MemoryOriginMessage),
            _ => throw new McpException(OriginMessage(questionId, answer.Origin)),
        };

        return new OrderAnswer(
            questionId,
            string.IsNullOrWhiteSpace(answer.OptionId) ? null : answer.OptionId.Trim(),
            string.IsNullOrWhiteSpace(answer.FreeText) ? null : answer.FreeText.Trim(),
            answer.DecideForMe,
            origin,
            RememberChoice.No);
    }

    private static string OriginMessage(string questionId, string? origin) =>
        string.IsNullOrWhiteSpace(origin)
            ? $"The answer to {Shorten(questionId)} has no origin. Set origin to \"user\" when the person gave the answer, \"client\" when you took it from the conversation yourself."
            : $"The answer to {Shorten(questionId)} has the unknown origin \"{Shorten(origin.Trim())}\". Use \"user\" when the person gave the answer, \"client\" when you took it from the conversation yourself.";

    private static string Shorten(string value) => value.Length <= 20 ? value : value[..20] + "…";
}
