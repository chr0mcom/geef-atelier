using Geef.Atelier.Core.Notifications;

namespace Geef.Atelier.Web.Notifications;

/// <summary>DI registration for live run updates (the in-process feed for components and its notifier).</summary>
public static class LiveUpdateServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IRunUpdateFeed"/>, the <see cref="IRunNotifier"/> that feeds both (also serving as
    /// <see cref="IOrderNotifier"/>), and the
    /// per-circuit <see cref="Components.CircuitDataGate"/> that keeps live reloads and user actions from using the
    /// circuit's database context at the same time.
    /// </summary>
    public static IServiceCollection AddRunLiveUpdates(this IServiceCollection services)
    {
        services.AddScoped<Components.CircuitDataGate>();
        services.AddSingleton<IRunUpdateFeed, InProcessRunUpdateFeed>();
        services.AddSingleton<LiveRunNotifier>();
        services.AddSingleton<IRunNotifier>(sp => sp.GetRequiredService<LiveRunNotifier>());
        services.AddSingleton<IOrderNotifier>(sp => sp.GetRequiredService<LiveRunNotifier>());
        return services;
    }
}
