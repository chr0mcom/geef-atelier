using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Web.Components.UI.Orders;

/// <summary>What the person entered on the change card and has not been made yet: their words and the chosen quick buttons.</summary>
public sealed record OrderChangeDraft(string Text, IReadOnlyList<RevisionQuick> Quick);
