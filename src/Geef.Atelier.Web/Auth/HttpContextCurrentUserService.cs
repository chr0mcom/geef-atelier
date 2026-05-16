using System.Security.Claims;
using Geef.Atelier.Application.Auth;
using Microsoft.AspNetCore.Http;

namespace Geef.Atelier.Web.Auth;

internal sealed class HttpContextCurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public string? Username => httpContextAccessor.HttpContext?.User.Identity?.Name;

    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public bool IsAdmin => httpContextAccessor.HttpContext?.User.IsInRole("admin") ?? false;
}
