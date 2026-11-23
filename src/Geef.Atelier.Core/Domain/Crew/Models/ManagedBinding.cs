namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>Provider/model binding of a profile plus its optional model-matrix assignment.</summary>
/// <param name="Provider">Provider name.</param>
/// <param name="Model">Model identifier.</param>
/// <param name="Slot">Role in the model matrix; <c>null</c> means the model was chosen manually.</param>
/// <param name="Domain">Profile-level domain override; <c>null</c> inherits the crew's domain.</param>
public sealed record ManagedBinding(string Provider, string Model, ModelSlot? Slot, ModelDomain? Domain);
