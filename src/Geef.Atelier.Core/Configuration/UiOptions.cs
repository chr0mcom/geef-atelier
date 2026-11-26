namespace Geef.Atelier.Core.Configuration;

/// <summary>What the web interface shows to whom (section <c>Ui</c>).</summary>
public sealed class UiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ui";

    /// <summary>
    /// Whether the entries that lead to the workshop pages (the menu "Workshop", the link to the older form on the start page,
    /// the technical details of an order and the way back to the workshop on the account pages) are shown only to administrators.
    /// Pages, routes, access rights and data stay unchanged; a person who opens such an address directly still gets the page.
    /// </summary>
    public bool WorkshopForAdminsOnly { get; set; } = true;
}
