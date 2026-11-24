using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Application.Profiles;

/// <summary>DI registration of the profile service.</summary>
public static class ProfileServiceExtensions
{
    /// <summary>Registers <see cref="IProfileService"/>. Empty until the profile service is built.</summary>
    public static IServiceCollection AddProfiles(this IServiceCollection services) => services;
}
