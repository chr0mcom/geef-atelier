using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geef.Atelier.Application.Profiles;

/// <summary>DI registration of the profile service.</summary>
public static class ProfileServiceExtensions
{
    /// <summary>Registers <see cref="IProfileService"/>; the repository comes from the persistence registration.</summary>
    public static IServiceCollection AddProfiles(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IProfileService, ProfileService>();
        return services;
    }
}
