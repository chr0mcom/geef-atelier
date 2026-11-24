namespace Geef.Atelier.Mcp.Models;

/// <summary>A question Atelier asks before writing.</summary>
/// <param name="Id">Id to pass as <c>questionId</c> to <c>answer_order</c>.</param>
/// <param name="Topic">What the question is about, snake_case (for example <c>length</c> or <c>audience</c>).</param>
/// <param name="Text">The question in the language of the request.</param>
/// <param name="Options">Two to four answer options.</param>
/// <param name="DefaultOptionId">Option Atelier would choose itself, if any.</param>
/// <param name="AllowFreeText">Whether the person may answer in their own words.</param>
public sealed record QuestionDto(string Id, string Topic, string Text, IReadOnlyList<QuestionOptionDto> Options, string? DefaultOptionId, bool AllowFreeText);
