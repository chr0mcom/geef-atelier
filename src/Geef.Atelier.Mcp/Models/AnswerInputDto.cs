using System.ComponentModel;

namespace Geef.Atelier.Mcp.Models;

/// <summary>One answer passed to <c>answer_order</c>.</summary>
public sealed record AnswerInputDto(
    [property: Description("Id of the question (q1, q2, q3, length), of an assumption to correct (a1, a2, …), or \"revision\" to answer whether the file in revisionFile should be improved.")] string QuestionId,
    [property: Description("Who decided this answer: \"user\" when the person said it, \"client\" when you answered it yourself from the conversation.")] string Origin,
    [property: Description("Id of the chosen option, if any; for \"revision\" \"yes\" (improve the file) or \"no\" (write something new, use the file only as material).")] string? OptionId = null,
    [property: Description("The person's own words, kept short (the error names the limit if it is too long); for an assumption the correction. Only for questions with allowFreeText.")] string? FreeText = null,
    [property: Description("True when the person leaves this question to Atelier; then send neither optionId nor freeText.")] bool DecideForMe = false);
