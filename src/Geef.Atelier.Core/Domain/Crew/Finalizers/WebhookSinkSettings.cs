namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>
/// Typed wrapper over the <c>Settings</c> dictionary for <see cref="FinalizerType.ExternalSink"/>
/// profiles that target a webhook endpoint.
/// <c>SinkKind</c> must be <c>"webhook"</c> to activate this executor branch.
/// </summary>
public sealed record WebhookSinkSettings(
    string Url,
    string? AuthHeader,
    string ContentType,
    int TimeoutSeconds)
{
    public const string SinkKindValue = "webhook";
    public const string KeySinkKind = "SinkKind";
    public const string KeyUrl = "Url";
    public const string KeyAuthHeader = "AuthHeader";
    public const string KeyContentType = "ContentType";
    public const string KeyTimeoutSeconds = "TimeoutSeconds";

    public static WebhookSinkSettings From(Dictionary<string, string> settings) => new(
        Url: settings.GetValueOrDefault(KeyUrl, string.Empty),
        AuthHeader: settings.GetValueOrDefault(KeyAuthHeader),
        ContentType: settings.GetValueOrDefault(KeyContentType, "application/json"),
        TimeoutSeconds: int.TryParse(settings.GetValueOrDefault(KeyTimeoutSeconds), out var t) ? t : 30);

    public Dictionary<string, string> ToDict()
    {
        var dict = new Dictionary<string, string>
        {
            [KeySinkKind] = SinkKindValue,
            [KeyUrl] = Url,
            [KeyContentType] = ContentType,
            [KeyTimeoutSeconds] = TimeoutSeconds.ToString(),
        };
        if (AuthHeader is not null)
            dict[KeyAuthHeader] = AuthHeader;
        return dict;
    }
}
