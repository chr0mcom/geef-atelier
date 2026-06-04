using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Composition;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Parses a raw Crew-Spec JSON string into a <see cref="CrewSpecArtifact"/> domain record.
/// Returns <see langword="null"/> when the JSON is missing required top-level structure.
/// </summary>
internal static class CrewSpecParser
{
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Attempts to parse <paramref name="json"/> into a <see cref="CrewSpecArtifact"/>.
    /// </summary>
    /// <param name="json">Raw Crew-Spec artifact JSON.</param>
    /// <returns>
    /// A populated <see cref="CrewSpecArtifact"/> on success, or <see langword="null"/> when
    /// the JSON is malformed or lacks a recognisable <c>mode</c> field.
    /// </returns>
    public static CrewSpecArtifact? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc  = JsonDocument.Parse(json, ParseOptions);
            var root = doc.RootElement;

            // Determine mode — accept both plain string and nested object with "mode" key.
            var modeStr = root.TryGetProperty("mode", out var modeEl)
                ? modeEl.GetString()
                : null;

            var mode = modeStr?.Trim().ToLowerInvariant() switch
            {
                "existingtemplate" or "existing_template" or "existing-template" =>
                    CrewSpecMode.ExistingTemplate,
                "composed" or "new" => CrewSpecMode.Composed,
                _ => (CrewSpecMode?)null,
            };

            if (mode is null)
                return null;

            if (mode == CrewSpecMode.ExistingTemplate)
            {
                var templateName = root.TryGetProperty("existing_template_name", out var tnEl)
                    ? tnEl.GetString()
                    : root.TryGetProperty("existingTemplateName", out var tnEl2)
                        ? tnEl2.GetString()
                        : null;

                return new CrewSpecArtifact
                {
                    Mode                 = CrewSpecMode.ExistingTemplate,
                    ExistingTemplateName = templateName,
                };
            }

            // Composed mode — parse all profile references.
            var executor = ParseProfileRef(root, "executor");

            var reviewers        = ParseProfileRefList(root, "reviewers");
            var finalizers       = ParseProfileRefList(root, "finalizers");
            var advisors         = ParseProfileRefList(root, "advisors");
            var groundingProviders = ParseProfileRefList(root, "grounding_providers")
                                    ?? ParseProfileRefList(root, "groundingProviders")
                                    ?? [];

            return new CrewSpecArtifact
            {
                Mode              = mode.Value,
                Executor          = executor,
                Reviewers         = reviewers        ?? [],
                Finalizers        = finalizers       ?? [],
                Advisors          = advisors         ?? [],
                GroundingProviders = groundingProviders,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // -------------------------------------------------------------------------

    private static CrewSpecProfileRef? ParseProfileRef(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;

        return ParseSingleRef(el);
    }

    private static IReadOnlyList<CrewSpecProfileRef>? ParseProfileRefList(
        JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var el))
            return null;

        if (el.ValueKind == JsonValueKind.Array)
        {
            var result = new List<CrewSpecProfileRef>();
            foreach (var item in el.EnumerateArray())
            {
                var parsed = ParseSingleRef(item);
                if (parsed is not null)
                    result.Add(parsed);
            }
            return result;
        }

        return null;
    }

    private static CrewSpecProfileRef? ParseSingleRef(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;

        // Reuse reference: { "reuse": "profile-name" }
        if (el.TryGetProperty("reuse", out var reuseEl) && reuseEl.ValueKind == JsonValueKind.String)
        {
            return new CrewSpecProfileRef { Reuse = reuseEl.GetString() };
        }

        // Inline profile definition
        var name         = GetStringOrNull(el, "name");
        var systemPrompt = GetStringOrNull(el, "system_prompt") ?? GetStringOrNull(el, "systemPrompt");
        var provider     = GetStringOrNull(el, "provider");
        var model        = GetStringOrNull(el, "model");

        if (name is null && systemPrompt is null && provider is null && model is null)
            return null;

        return new CrewSpecProfileRef
        {
            Name         = name,
            SystemPrompt = systemPrompt,
            Provider     = provider,
            Model        = model,
        };
    }

    private static string? GetStringOrNull(JsonElement el, string propertyName) =>
        el.TryGetProperty(propertyName, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
