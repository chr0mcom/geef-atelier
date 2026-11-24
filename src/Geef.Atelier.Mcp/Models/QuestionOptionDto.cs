namespace Geef.Atelier.Mcp.Models;

/// <summary>A clickable answer option of a question.</summary>
/// <param name="Id">Id to pass as <c>optionId</c> to <c>answer_order</c>.</param>
/// <param name="Label">Text of the option in the language of the request.</param>
public sealed record QuestionOptionDto(string Id, string Label);
