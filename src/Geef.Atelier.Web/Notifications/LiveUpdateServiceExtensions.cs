using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Web.Hubs;

namespace Geef.Atelier.Web.Notifications;

/// <summary>DI registration for live run updates (SignalR hub notifier plus the in-process feed for components).</summary>
public static class LiveUpdateServiceExtensions
{
    /// <summary>
    /// Registers SignalR, <see cref="IRunUpdateFeed"/>, the <see cref="IRunNotifier"/> that feeds both (also serving as
    /// <see cref="IOrderNotifier"/>), and the
    /// per-circuit <see cref="Components.CircuitDataGate"/> that keeps live reloads and user actions from using the
    /// circuit's database context at the same time.
    /// </summary>
    public static IServiceCollection AddRunLiveUpdates(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddScoped<Components.CircuitDataGate>();
        services.AddSingleton<IRunUpdateFeed, InProcessRunUpdateFeed>();
        services.AddSingleton<LiveRunNotifier>();
        services.AddSingleton<IRunNotifier>(sp => sp.GetRequiredService<LiveRunNotifier>());
        services.AddSingleton<IOrderNotifier>(sp => sp.GetRequiredService<LiveRunNotifier>());
        return services;
    }

    /// <summary>Hub route used by <see cref="MapRunHub"/>.</summary>
    public const string RunHubPath = "/hubs/runs";

    /// <summary>
    /// Maps <see cref="RunHub"/> for signed-in users only. A connection is closed as soon as the authentication it was
    /// opened with expires, so an expired session never keeps receiving run or dashboard events.
    /// </summary>
    public static HubEndpointConventionBuilder MapRunHub(this IEndpointRouteBuilder endpoints)
    {
        var hub = endpoints.MapHub<RunHub>(RunHubPath, options => options.CloseOnAuthenticationExpiration = true);
        hub.RequireAuthorization();
        return hub;
    }
}
