namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>
/// Typed wrapper over the <c>Settings</c> dictionary for <see cref="FinalizerType.FileExport"/> profiles.
/// Valid <c>Format</c> values: <c>markdown</c>, <c>html</c>, <c>pdf</c>, <c>docx</c>, <c>txt</c>, <c>json</c>.
/// </summary>
public sealed record FileExportSettings(string Format)
{
    public const string KeyFormat = "Format";

    public static FileExportSettings From(Dictionary<string, string> settings) =>
        new(settings.GetValueOrDefault(KeyFormat, "markdown"));

    public Dictionary<string, string> ToDict() =>
        new() { [KeyFormat] = Format };
}
