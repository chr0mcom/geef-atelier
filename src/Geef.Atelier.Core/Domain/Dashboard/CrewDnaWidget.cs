namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Breakdown of how often each crew template has been used (30-day window).</summary>
public sealed record CrewDna(IReadOnlyList<CrewDnaEntry> Entries);

public sealed record CrewDnaEntry(string TemplateName, string DisplayName, int Count, double Share);
