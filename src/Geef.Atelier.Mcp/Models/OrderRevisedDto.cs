namespace Geef.Atelier.Mcp.Models;

/// <summary>Answer of <c>revise_order</c>.</summary>
/// <param name="OrderId">Order id.</param>
/// <param name="State">Display state on the wire, as in <see cref="OrderStatusDto.State"/>; that of the new version once it was started.</param>
/// <param name="Version">The version being written: the new one, or the one already being written when a change was asked for before.</param>
/// <param name="Notices">Sentences for the person, for example that a change is already being made; empty when there is nothing to tell.</param>
/// <param name="Message">What the client should do next, in plain English.</param>
public sealed record OrderRevisedDto(string OrderId, string State, int? Version, IReadOnlyList<string> Notices, string? Message = null);
