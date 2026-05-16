namespace Geef.Atelier.Application.Auth;

/// <summary>Provides the identity of the currently authenticated user.</summary>
public interface ICurrentUserService
{
    /// <summary>The authenticated user's username, or null if not authenticated.</summary>
    string? Username { get; }

    /// <summary>Whether the user is currently authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Whether the user has the admin role.</summary>
    bool IsAdmin { get; }
}
