using System.ComponentModel;

namespace Geef.Atelier.Mcp.Models;

/// <summary>One answer passed to <c>answer_order</c>.</summary>
public sealed record AnswerInputDto(
    [property: Description("Id of the question (q1, q2, q3, length) or of an assumption to correct (a1, a2, …).")] string QuestionId,
    [property: Description("Who decided this answer: \"user\" when the person said it, \"client\" when you answered it yourself from the conversation.")] string Origin,
    [property: Description("Id of the chosen option, if any.")] string? OptionId = null,
    [property: Description("The person's own words, up to 500 characters; for an assumption the correction.")] string? FreeText = null,
    [property: Description("True when the person leaves this question to Atelier.")] bool DecideForMe = false);
