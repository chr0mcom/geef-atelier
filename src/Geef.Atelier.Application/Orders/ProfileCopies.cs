using System.Text.Json;
using System.Text.Json.Nodes;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Application.Orders;

/// <summary>
/// Pure: what the fact check may have copied from the About me details into a run's report and findings, and how it looks for
/// someone who may not see the profile. Instructions and material passages that stand in the profile are replaced, and the
/// reasons of the fact check, which may repeat the profile in the model's own words, are replaced by a fixed sentence.
/// </summary>
public static class ProfileCopies
{
    /// <summary>Stands in for a passage of the About me details.</summary>
    public const string HiddenPassage = "[from the About me details, hidden here]";

    /// <summary>Stands in for a reason of the fact check that may repeat the About me details.</summary>
    public const string HiddenReason = "The reason of the fact check is hidden here because it may repeat the About me details of the person who ordered this text.";

    /// <summary>
    /// <paramref name="report"/> without what its fact check took from <paramref name="profile"/>: every reason is replaced by
    /// <see cref="HiddenReason"/>, instructions with origin <see cref="InstructionOrigin.Memory"/> and material passages found in the
    /// profile part (or, in older reports without a part, standing in <paramref name="profile"/>) by <see cref="HiddenPassage"/>.
    /// A report without a fact check comes back unchanged.
    /// </summary>
    public static RunReport WithoutProfile(RunReport report, string? profile)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (report.FactCheck is not { } factCheck)
            return report;
        FactFinding Clean(FactFinding f) => f with
        {
            WhatIsWrong = HiddenReason,
            InstructionQuote = f.Origin == InstructionOrigin.Memory || InProfile(f.InstructionQuote, profile) ? HiddenPassage : f.InstructionQuote,
            MaterialQuote = FromProfile(f.MaterialFrom, f.MaterialQuote, profile) ? HiddenPassage : f.MaterialQuote,
            MaterialQuote2 = FromProfile(f.Material2From, f.MaterialQuote2, profile) ? HiddenPassage : f.MaterialQuote2,
        };
        return report with
        {
            FactCheck = factCheck with
            {
                Open = factCheck.Open.Select(Clean).ToList(),
                SourceConflicts = factCheck.SourceConflicts.Select(Clean).ToList(),
                Corrected = factCheck.Corrected.Select(Clean).ToList(),
            },
        };
    }

    /// <summary>
    /// The findings of a run as <paramref name="viewer"/> may see them: unchanged for the owner and for a run without a profile
    /// block; otherwise the fact check's reasons become <see cref="HiddenReason"/> and quotes that stand in the profile
    /// <see cref="HiddenPassage"/>.
    /// </summary>
    public static IReadOnlyList<FindingEntity> ForViewer(IReadOnlyList<FindingEntity> findings, string briefing, string? owner, string? viewer)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (owner is not null && string.Equals(owner, viewer, StringComparison.Ordinal))
            return findings;
        var profile = BriefingSections.Parse(briefing ?? string.Empty).Profile;
        if (profile is null)
            return findings;
        return findings.Select(f => Hidden(f, profile)).ToList();
    }

    /// <summary><paramref name="finding"/> of the fact check without what may come from <paramref name="profile"/>; other findings unchanged.</summary>
    public static FindingEntity Hidden(FindingEntity finding, string? profile) =>
        finding.Category == FactFindingMetadata.FindingCategory
            ? finding with { Message = HiddenReason, Quote = HiddenQuote(finding.Quote, profile) }
            : finding;

    /// <summary>
    /// The JSON payload of a stored pipeline event without the About me details: every text at any depth that holds the profile
    /// block of a briefing (the input of the run in <c>PipelineStartedEvent.Input</c>, or a question that repeats the briefing) loses
    /// it as in <see cref="BriefingSections.WithoutProfile"/>, every other copy of <paramref name="profile"/> word for word becomes
    /// <see cref="BriefingSections.ProfileHiddenNote"/>, and in every finding of the fact check (category <c>fact-check</c>) the
    /// reason becomes <see cref="HiddenReason"/>, an instruction with origin <c>Memory</c> and every quote that stands in the profile
    /// <see cref="HiddenPassage"/>. Without <paramref name="profile"/>, the profile of a block in the payload is used. <c>null</c> when
    /// nothing changes or the payload is no JSON.
    /// </summary>
    public static string? WithoutProfileInEvent(string? payloadJson, string? profile)
    {
        if (Parse(payloadJson) is not { } root)
            return null;
        profile = string.IsNullOrWhiteSpace(profile) ? ProfileOf(root) : profile;
        var changed = CleanTexts(root, profile);
        changed |= CleanNode(root, profile);
        return changed ? root.ToJsonString() : null;
    }

    /// <summary>The About me details of the first briefing with a profile block that the JSON payload of a stored event holds, else <c>null</c>.</summary>
    public static string? ProfileInEvent(string? payloadJson) => Parse(payloadJson) is { } root ? ProfileOf(root) : null;

    private static JsonNode? Parse(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return null;
        try
        {
            return JsonNode.Parse(payloadJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ProfileOf(JsonNode root) =>
        Texts(root).Where(HoldsProfileBlock).Select(t => BriefingSections.Parse(t).Profile).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

    private static bool HoldsProfileBlock(string text) =>
        text.Contains("\n" + BriefingSections.ProfileHeading + "\n", StringComparison.Ordinal);

    private static IEnumerable<string> Texts(JsonNode? node) => node switch
    {
        JsonObject obj => obj.SelectMany(p => Texts(p.Value)),
        JsonArray array => array.SelectMany(Texts),
        JsonValue value when value.TryGetValue<string>(out var text) => [text],
        _ => [],
    };

    private static bool CleanTexts(JsonNode node, string? profile)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    if (child is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        if (WithoutProfileText(text, profile) is { } cleaned)
                        {
                            obj[key] = cleaned;
                            changed = true;
                        }
                    }
                    else if (child is not null)
                    {
                        changed |= CleanTexts(child, profile);
                    }
                }
                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text))
                    {
                        if (WithoutProfileText(text, profile) is { } cleaned)
                        {
                            array[i] = cleaned;
                            changed = true;
                        }
                    }
                    else if (array[i] is { } child)
                    {
                        changed |= CleanTexts(child, profile);
                    }
                }
                break;
        }
        return changed;
    }

    private static string? WithoutProfileText(string text, string? profile)
    {
        var cleaned = HoldsProfileBlock(text) ? BriefingSections.WithoutProfile(text) : text;
        if (!string.IsNullOrWhiteSpace(profile) && profile.Trim().Length >= FactFindingFilter.MinMaterialChars)
            cleaned = cleaned.Replace(profile.Trim(), BriefingSections.ProfileHiddenNote, StringComparison.Ordinal);
        return string.Equals(cleaned, text, StringComparison.Ordinal) ? null : cleaned;
    }

    /// <summary><see cref="HiddenPassage"/> when <paramref name="quote"/> stands in <paramref name="profile"/>, else <paramref name="quote"/>.</summary>
    public static string? HiddenQuote(string? quote, string? profile) => InProfile(quote, profile) ? HiddenPassage : quote;

    private static bool CleanNode(JsonNode node, string? profile)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                if (Text(obj, "Category") == FactFindingMetadata.FindingCategory)
                    changed |= CleanFinding(obj, profile);
                foreach (var child in obj.Select(p => p.Value).OfType<JsonNode>().ToList())
                    changed |= CleanNode(child, profile);
                break;
            case JsonArray array:
                foreach (var child in array.OfType<JsonNode>().ToList())
                    changed |= CleanNode(child, profile);
                break;
        }
        return changed;
    }

    private static bool CleanFinding(JsonObject finding, string? profile)
    {
        var changed = Replace(finding, "Message", _ => HiddenReason);
        if (Child(finding, "Metadata") is JsonObject metadata)
        {
            var memory = string.Equals(Text(metadata, FactFindingMetadata.Origin), nameof(InstructionOrigin.Memory), StringComparison.OrdinalIgnoreCase);
            changed |= Replace(metadata, FactFindingMetadata.Instruction, v => memory || InProfile(v, profile) ? HiddenPassage : v);
            foreach (var key in new[] { FactFindingMetadata.Quote, FactFindingMetadata.Material, FactFindingMetadata.Material2 })
                changed |= Replace(metadata, key, v => HiddenQuote(v, profile) ?? v);
        }
        return changed;
    }

    private static string? Key(JsonObject obj, string name) =>
        obj.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));

    private static JsonNode? Child(JsonObject obj, string name) => Key(obj, name) is { } key ? obj[key] : null;

    private static string? Text(JsonObject obj, string name) =>
        Child(obj, name) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool Replace(JsonObject obj, string name, Func<string, string> replace)
    {
        if (Key(obj, name) is not { } key || Text(obj, key) is not { } current)
            return false;
        var replaced = replace(current);
        if (string.Equals(replaced, current, StringComparison.Ordinal))
            return false;
        obj[key] = replaced;
        return true;
    }

    private static bool FromProfile(MaterialPart? part, string? quote, string? profile) =>
        part == MaterialPart.Profile || part is null && InProfile(quote, profile);

    private static bool InProfile(string? quote, string? profile) =>
        !string.IsNullOrWhiteSpace(quote) && !string.IsNullOrWhiteSpace(profile)
        && QuoteVerifier.FindVerbatim(profile, quote.Trim(), FactFindingFilter.MinMaterialChars).IsVerbatim;
}
