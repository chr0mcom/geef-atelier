using Geef.Atelier.Core.Notifications;

namespace Geef.Atelier.Web.Notifications;

/// <summary>DI registration for live run updates (SignalR hub notifier plus the in-process feed for components).</summary>
public static class LiveUpdateServiceExtensions
{
    /// <summary>Registers SignalR, <see cref="IRunUpdateFeed"/> and the <see cref="IRunNotifier"/> that feeds both.</summary>
    public static IServiceCollection AddRunLiveUpdates(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<IRunUpdateFeed, InProcessRunUpdateFeed>();
        services.AddSingleton<IRunNotifier, SignalRRunNotifier>();
        return services;
    }
}
