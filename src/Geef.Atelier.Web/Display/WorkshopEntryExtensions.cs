using System.Security.Claims;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Web.Notifications;

namespace Geef.Atelier.Web.Display;

/// <summary>Decides whether the entries to the workshop pages are shown (<see cref="UiOptions.WorkshopForAdminsOnly"/>).</summary>
public static class WorkshopEntryExtensions
{
    /// <summary>Where the account pages lead back to when the workshop entries are hidden.</summary>
    public const string LayBackLink = "/work";

    /// <summary>True when <paramref name="user"/> sees the entries: always with the switch off, otherwise only administrators.</summary>
    public static bool ShowsWorkshopEntriesTo(this UiOptions options, ClaimsPrincipal? user) =>
        !options.WorkshopForAdminsOnly || user.IsAtelierAdmin();
}
