namespace Geef.Atelier.Web.Components.UI;

/// <summary>Data returned by <see cref="SubmitForm"/> when the user submits a valid run.</summary>
public sealed record SubmitFormResult(string Briefing, string ConfigJson);
