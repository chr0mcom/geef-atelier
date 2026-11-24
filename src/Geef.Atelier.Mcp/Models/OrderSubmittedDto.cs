namespace Geef.Atelier.Mcp.Models;

/// <summary>
/// Answer of <c>submit_order</c>. New fields are only ever appended with defaults, so clients may ignore what they do not know.
/// A later stage answers with state <c>needs_input</c> and no <paramref name="RunId"/> when the order needs answers first.
/// </summary>
/// <param name="OrderId">Id of the new order; pass it to <c>get_order_status</c> and <c>get_order_result</c>.</param>
/// <param name="State">Display state on the wire, for example <c>queued</c>, <c>working</c> or <c>needs_input</c>.</param>
/// <param name="RunId">Id of the first run of the order, if one was started.</param>
/// <param name="Message">What the client should do next, in plain English.</param>
/// <param name="Sources">One receipt per attached file, in the order they were attached.</param>
public sealed record OrderSubmittedDto(string OrderId, string State, string? RunId, string Message, IReadOnlyList<OrderSourceDto> Sources);
