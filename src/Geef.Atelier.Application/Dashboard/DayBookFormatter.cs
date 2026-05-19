using Geef.Atelier.Core.Domain.Dashboard;

namespace Geef.Atelier.Application.Dashboard;

/// <summary>Produces human-readable verb strings for each <see cref="DayBookKind"/>.</summary>
public static class DayBookFormatter
{
    public static string GetVerb(DayBookKind kind) => kind switch
    {
        DayBookKind.RunCompleted     => "Completed",
        DayBookKind.RunFailed        => "Failed",
        DayBookKind.DocumentIndexed  => "Indexed",
        DayBookKind.ProviderAdded    => "Added provider",
        DayBookKind.OAuthClientAdded => "Registered client",
        DayBookKind.TemplateCreated  => "Created template",
        DayBookKind.UserAdded        => "Added user",
        _                            => "Updated"
    };
}
