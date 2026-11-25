using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Application.Orders;

/// <summary>
/// A change of the shown version of an order, through the web ("Change something") or MCP (<c>revise_order</c>): the person's own words
/// in <see cref="Change"/> and/or quick buttons in <see cref="Quick"/>; at least one of both.
/// </summary>
public sealed record ReviseOrderRequest(Guid OrderId, string? RequestingUsername, string? Change, IReadOnlyList<RevisionQuick> Quick, OrderEntry Entry);

/// <summary>Answer to a change: the display state of the order, the run and version of the new version (if one was started) and notices for the person.</summary>
public sealed record OrderRevision(Guid OrderId, OrderDisplayState State, Guid? RunId, int? Version, IReadOnlyList<string> Notices);

/// <summary>Raised for a change that cannot be made (switched off, no instruction, too long, too many versions, nothing to change). The message is plain English for the UI and MCP.</summary>
public sealed class OrderRevisionException(string message) : InvalidOperationException(message);
