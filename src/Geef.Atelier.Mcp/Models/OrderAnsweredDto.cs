namespace Geef.Atelier.Mcp.Models;

/// <summary>Answer of <c>answer_order</c>.</summary>
/// <param name="OrderId">Id of the order.</param>
/// <param name="State">Display state on the wire, for example <c>queued</c> or <c>working</c>.</param>
/// <param name="RunId">Id of the first run, once writing has started.</param>
/// <param name="Message">What the client should do next, in plain English.</param>
public sealed record OrderAnsweredDto(string OrderId, string State, string? RunId, string Message);
