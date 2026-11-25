using System.Globalization;
using System.Text.Json;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Infrastructure.Intake;

/// <summary>Reads the clarifier answer leniently into <see cref="ClarifierOutput"/>.</summary>
internal static class ClarifierOutputParser
{
    /// <summary>Longest understanding kept.</summary>
    public const int MaxUnderstandingChars = 400;

    /// <summary>Longest assumption kept.</summary>
    public const int MaxAssumptionChars = 200;

    /// <summary>Longest question kept before the gate; the gate drops everything over 120.</summary>
    public const int MaxQuestionChars = 300;

    /// <summary>Longest option kept before the gate; the gate drops everything over 60.</summary>
    public const int MaxOptionChars = 120;

    /// <summary>Longest text type kept.</summary>
    public const int MaxTextTypeChars = 80;

    /// <summary>Longest research topic kept.</summary>
    public const int MaxResearchTopicChars = 120;

    /// <summary>Most gaps kept.</summary>
    public const int MaxGaps = 8;

    /// <summary>Most research topics kept.</summary>
    public const int MaxResearchTopics = 5;

    /// <summary>Most options read per question; more still reach the gate as "too many".</summary>
    public const int MaxOptionsRead = 8;

    /// <summary>Tolerant parse: unknown enum values become Other/Assume, strings are trimmed and capped (understanding 400, assumption 200, question 300 before the gate), at most 8 gaps and 5 topics. Null when understanding, language or gaps are missing.</summary>
    public static ClarifierOutput? Parse(string json)
    {
        if (ExtractObject(json) is not { } text)
            return null;

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ClarifierOutput? Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var understanding = Cap(String(root, "understanding"), MaxUnderstandingChars);
        var language = Language(String(root, "language"));
        if (understanding is null || language is null || !root.TryGetProperty("gaps", out var gapsElement) || gapsElement.ValueKind != JsonValueKind.Array)
            return null;

        var gaps = gapsElement.EnumerateArray()
            .Where(g => g.ValueKind == JsonValueKind.Object)
            .Take(MaxGaps)
            .Select(ReadGap)
            .ToList();

        var topics = root.TryGetProperty("research_topics", out var topicsElement) && topicsElement.ValueKind == JsonValueKind.Array
            ? topicsElement.EnumerateArray()
                .Select(t => t.ValueKind == JsonValueKind.String ? Cap(t.GetString(), MaxResearchTopicChars) : null)
                .OfType<string>()
                .Take(MaxResearchTopics)
                .ToList()
            : [];

        return new ClarifierOutput(
            Cap(String(root, "text_type"), MaxTextTypeChars) ?? string.Empty,
            language,
            understanding,
            Bool(root, "is_revision") ?? false,
            Int(root, "revision_file"),
            Scale(String(root, "length_scale")),
            gaps,
            topics);
    }

    private static IntakeGap ReadGap(JsonElement gap) => new(
        Enum(String(gap, "topic"), GapTopic.Other),
        Enum(String(gap, "resolution"), GapResolution.Assume),
        Bool(gap, "user_can_know") ?? false,
        Bool(gap, "web_researchable") ?? false,
        Enum(String(gap, "impact"), GapImpact.Low),
        Cap(String(gap, "assumption"), MaxAssumptionChars),
        Enum(String(gap, "confidence"), AssumptionConfidence.Unsure),
        gap.TryGetProperty("question", out var question) && question.ValueKind == JsonValueKind.Object ? ReadQuestion(question) : null);

    private static ProposedQuestion ReadQuestion(JsonElement question)
    {
        var options = question.TryGetProperty("options", out var optionsElement) && optionsElement.ValueKind == JsonValueKind.Array
            ? optionsElement.EnumerateArray()
                .Take(MaxOptionsRead)
                .Select(o => o.ValueKind == JsonValueKind.String ? Cap(o.GetString(), MaxOptionChars) ?? string.Empty : string.Empty)
                .ToList()
            : [];
        var number = Int(question, "default_option");
        return new ProposedQuestion(
            Cap(String(question, "text"), MaxQuestionChars) ?? string.Empty,
            options,
            number is > 0 ? number - 1 : null,
            Bool(question, "allow_free_text") ?? true);
    }

    private static string? ExtractObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        return start >= 0 && end > start ? json[start..(end + 1)] : null;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool? Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        } : null;

    private static int? Int(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number is >= int.MinValue and <= int.MaxValue)
            return (int)Math.Round(number);
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static string? Language(string? value)
    {
        var code = value?.Trim().Split('-', '_')[0].ToLowerInvariant();
        return code is { Length: 2 } && code.All(char.IsAsciiLetterLower) ? code : null;
    }

    private static LengthScale Scale(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "short_form" => LengthScale.ShortForm,
        "long_form" => LengthScale.LongForm,
        _ => LengthScale.Standard,
    };

    private static T Enum<T>(string? value, T fallback) where T : struct, System.Enum =>
        value is not null
        && System.Enum.TryParse<T>(value.Trim().Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var parsed)
        && System.Enum.IsDefined(parsed)
        && !int.TryParse(value, out _)
            ? parsed
            : fallback;

    private static string? Cap(string? value, int max)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (text.Length <= max)
            return text;
        var cut = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..cut].TrimEnd();
    }
}
