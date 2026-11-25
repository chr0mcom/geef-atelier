using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Core.Domain.Research;

/// <summary>
/// The rules for the address and the title of a page found on the web, in one place for the search, the entries, the prompts and the
/// page the person sees. An address is kept only when it is an absolute http or https address with a host, without user information
/// and at most <see cref="MaxChars"/> characters long; a title that holds a link, an address or a request for secrets
/// (<see cref="IntakeContentGuard.IsUnsafe"/>) is replaced by the host of its page.
/// </summary>
public static class ResearchAddress
{
    /// <summary>Longest address that is kept.</summary>
    public const int MaxChars = 500;

    /// <summary><paramref name="url"/> trimmed when it is an acceptable address (see <see cref="ResearchAddress"/>); otherwise <c>null</c>.</summary>
    public static string? Acceptable(string? url)
    {
        var trimmed = url?.Trim();
        return !string.IsNullOrEmpty(trimmed)
            && trimmed.Length <= MaxChars
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            ? trimmed
            : null;
    }

    /// <summary>The host of an acceptable address without a leading <c>www.</c>; <c>null</c> for anything else.</summary>
    public static string? Host(string? url)
    {
        if (Acceptable(url) is not { } address || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
            return null;
        var host = uri.IdnHost;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>
    /// The address as the writer and the fact check read it: scheme, host, port and path of an acceptable address, without query and
    /// fragment; empty for anything else.
    /// </summary>
    public static string ForPrompt(string? url) =>
        Acceptable(url) is { } address && Uri.TryCreate(address, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Path)
            : string.Empty;

    /// <summary>
    /// The title of a page as it is kept and shown: <paramref name="title"/> on one line (<see cref="ResearchTextExtensions.CleanTitle"/>),
    /// or the host of <paramref name="url"/> (empty without one) when the title is unsafe by <see cref="IntakeContentGuard.IsUnsafe"/>.
    /// </summary>
    public static string SafeTitle(string? title, string? url)
    {
        var clean = title.CleanTitle();
        return clean.Length > 0 && IntakeContentGuard.IsUnsafe(clean) ? Host(url) ?? string.Empty : clean;
    }
}
