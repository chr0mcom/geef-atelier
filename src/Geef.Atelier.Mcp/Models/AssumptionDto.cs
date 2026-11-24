namespace Geef.Atelier.Mcp.Models;

/// <summary>An assumption the writer uses unless the person corrects it.</summary>
/// <param name="Id">Id to pass as <c>questionId</c> to <c>answer_order</c> with the correction as <c>freeText</c>.</param>
/// <param name="Text">The assumption in the language of the request.</param>
/// <param name="Unsure">True when Atelier is not sure of it.</param>
public sealed record AssumptionDto(string Id, string Text, bool Unsure);
