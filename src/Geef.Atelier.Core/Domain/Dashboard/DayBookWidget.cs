namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Activity log entry rendered in the Day-Book stream.</summary>
public sealed record DayBookEntry(
    DayBookKind Kind,
    string Verb,
    string Subject,
    string? Detail,
    DateTimeOffset At);

public enum DayBookKind
{
    RunCompleted    = 0,
    RunFailed       = 1,
    DocumentIndexed = 2,
    ProviderAdded   = 3,
    OAuthClientAdded = 4,
    TemplateCreated = 5,
    UserAdded       = 6
}
