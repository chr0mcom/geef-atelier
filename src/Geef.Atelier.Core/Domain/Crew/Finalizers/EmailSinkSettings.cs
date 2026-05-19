namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>
/// Typed wrapper over the <c>Settings</c> dictionary for <see cref="FinalizerType.ExternalSink"/>
/// profiles that deliver the final text via email.
/// <c>SinkKind</c> must be <c>"email"</c> to activate this executor branch.
/// SMTP credentials are resolved from environment / options at runtime — never stored in <c>Settings</c>.
/// </summary>
public sealed record EmailSinkSettings(
    string ToAddress,
    string Subject,
    bool AttachAsFile,
    string AttachmentFormat)
{
    public const string SinkKindValue = "email";
    public const string KeySinkKind = "SinkKind";
    public const string KeyToAddress = "ToAddress";
    public const string KeySubject = "Subject";
    public const string KeyAttachAsFile = "AttachAsFile";
    public const string KeyAttachmentFormat = "AttachmentFormat";

    public static EmailSinkSettings From(Dictionary<string, string> settings) => new(
        ToAddress: settings.GetValueOrDefault(KeyToAddress, string.Empty),
        Subject: settings.GetValueOrDefault(KeySubject, "Geef.Atelier — Run Result"),
        AttachAsFile: bool.TryParse(settings.GetValueOrDefault(KeyAttachAsFile), out var a) && a,
        AttachmentFormat: settings.GetValueOrDefault(KeyAttachmentFormat, "markdown"));

    public Dictionary<string, string> ToDict() => new()
    {
        [KeySinkKind] = SinkKindValue,
        [KeyToAddress] = ToAddress,
        [KeySubject] = Subject,
        [KeyAttachAsFile] = AttachAsFile.ToString(),
        [KeyAttachmentFormat] = AttachmentFormat,
    };
}
