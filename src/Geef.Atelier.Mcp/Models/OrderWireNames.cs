using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Mcp.Models;

/// <summary>Names of order, quality and usage values as they appear in MCP answers (snake_case, stable). Unknown values throw instead of inventing a name.</summary>
internal static class OrderWireNames
{
    /// <summary>The display state as defined once in the core (<see cref="OrderStateResolver.ToWireName"/>).</summary>
    public static string ToWire(this OrderDisplayState state) => state.ToWireName();

    public static string ToWire(this SourceQuality quality) => quality switch
    {
        SourceQuality.Good => "good",
        SourceQuality.Partial => "partial",
        SourceQuality.Poor => "poor",
        SourceQuality.Unreadable => "unreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, null),
    };

    public static string ToWire(this SourceUsageMode mode) => mode switch
    {
        SourceUsageMode.FullText => "full_text",
        SourceUsageMode.FullTextWithMap => "full_text_with_map",
        SourceUsageMode.Excluded => "excluded",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    public const string OriginUser = "user";
    public const string OriginClient = "client";
    public const string OriginMemory = "memory";

    public static string ToWire(this GapTopic topic) => topic switch
    {
        GapTopic.Length => "length",
        GapTopic.Audience => "audience",
        GapTopic.Purpose => "purpose",
        GapTopic.Tone => "tone",
        GapTopic.Formality => "formality",
        GapTopic.Language => "language",
        GapTopic.Sender => "sender",
        GapTopic.Signature => "signature",
        GapTopic.Facts => "facts",
        GapTopic.Format => "format",
        GapTopic.Date => "date",
        GapTopic.Scope => "scope",
        GapTopic.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null),
    };

    public static string ToWire(this AnswerOrigin origin) => origin switch
    {
        AnswerOrigin.User => OriginUser,
        AnswerOrigin.Client => OriginClient,
        AnswerOrigin.Memory => OriginMemory,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null),
    };

    public const string PassageNotBacked = "not_backed";
    public const string PassageAgainstInstruction = "against_instruction";
    public const string PassageReplacedInFiles = "replaced_in_files";
    public const string PassageDiffersFromLookedUp = "differs_from_looked_up";

    /// <summary>Kind of a shown passage: <see cref="PassageDiffersFromLookedUp"/> for one the pages looked up say otherwise, otherwise its <see cref="FactFindingKind"/>.</summary>
    public static string ToWire(this OrderUnbackedPassage passage) =>
        passage.LookedUp ? PassageDiffersFromLookedUp : passage.Kind.ToWire();

    /// <summary>Kind of a flagged passage of the text. A contradiction between files is never a passage (it is a source issue), so it throws.</summary>
    public static string ToWire(this FactFindingKind kind) => kind switch
    {
        FactFindingKind.Unsupported => PassageNotBacked,
        FactFindingKind.Instruction => PassageAgainstInstruction,
        FactFindingKind.Superseded => PassageReplacedInFiles,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public const string FactCheckChecked = "checked";
    public const string FactCheckOpenPoints = "open_points";
    public const string FactCheckNotCompleted = "not_completed";
    public const string FactCheckNotChecked = "not_checked";
    public const string FactCheckVersionNotChecked = "version_not_checked";

    public const string InstructionFromRequest = "request";
    public const string InstructionFromAnswers = "answers";
    public const string InstructionFromAboutMe = "about_me";
    public const string InstructionFromAssistant = "assistant";

    /// <summary>Where an instruction stands, as in <see cref="OrderUnbackedPassageDto.InstructionFrom"/>.</summary>
    public static string ToWire(this InstructionOrigin origin) => origin switch
    {
        InstructionOrigin.Stated => InstructionFromRequest,
        InstructionOrigin.Answered => InstructionFromAnswers,
        InstructionOrigin.Memory => InstructionFromAboutMe,
        InstructionOrigin.Client => InstructionFromAssistant,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null),
    };

    /// <summary>State of the fact check; a check that was switched off or only ran in the background is <c>not_checked</c>, as the web page says.</summary>
    public static string ToWire(this FactCheckState state) => state switch
    {
        FactCheckState.Checked => FactCheckChecked,
        FactCheckState.OpenPoints => FactCheckOpenPoints,
        FactCheckState.NotCompleted => FactCheckNotCompleted,
        FactCheckState.VersionNotChecked => FactCheckVersionNotChecked,
        FactCheckState.Off or FactCheckState.NotShown => FactCheckNotChecked,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public const string QuickShorter = "shorter";
    public const string QuickLonger = "longer";
    public const string QuickMoreFormal = "more_formal";
    public const string QuickFriendlier = "friendlier";
    public const string QuickSimpler = "simpler";

    /// <summary>
    /// The wire names of the quick changes <c>revise_order</c> offers, in the order of the buttons on the web page. <see cref="QuickLonger"/>
    /// is not offered; it is still recognised so that it gets the sentence that a longer text needs new details.
    /// </summary>
    public static IReadOnlyList<string> QuickNames { get; } = RevisionQuickText.Offered.Select(q => q.ToWire()).ToList();

    /// <summary>Wire name of a quick change of <c>revise_order</c>.</summary>
    public static string ToWire(this RevisionQuick quick) => quick switch
    {
        RevisionQuick.Shorter => QuickShorter,
        RevisionQuick.Longer => QuickLonger,
        RevisionQuick.MoreFormal => QuickMoreFormal,
        RevisionQuick.Friendlier => QuickFriendlier,
        RevisionQuick.Simpler => QuickSimpler,
        _ => throw new ArgumentOutOfRangeException(nameof(quick), quick, null),
    };

    /// <summary>
    /// The quick change of a wire name, ignoring case, surrounding blanks and whether words are joined by <c>_</c>, <c>-</c> or a blank;
    /// the name of the web button ("Simpler words") counts as well. <c>null</c> for anything unknown.
    /// </summary>
    public static RevisionQuick? ParseRevisionQuick(string? wire)
    {
        var key = string.Join('_', (wire ?? string.Empty).Trim().ToLowerInvariant()
            .Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries));
        return key switch
        {
            QuickShorter => RevisionQuick.Shorter,
            QuickLonger => RevisionQuick.Longer,
            QuickMoreFormal => RevisionQuick.MoreFormal,
            QuickFriendlier => RevisionQuick.Friendlier,
            QuickSimpler or "simpler_words" => RevisionQuick.Simpler,
            _ => null,
        };
    }

    /// <summary>Wire name of the formality of the About me details; <c>null</c> for "it depends".</summary>
    public static string? ToWire(this Formality formality) => formality switch
    {
        Formality.Formal => "formal",
        Formality.Informal => "informal",
        Formality.Unset => null,
        _ => throw new ArgumentOutOfRangeException(nameof(formality), formality, null),
    };

    /// <summary>The origin of an answer from its wire name, ignoring case and surrounding blanks; <c>null</c> for anything unknown.</summary>
    public static AnswerOrigin? ParseAnswerOrigin(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        OriginUser => AnswerOrigin.User,
        OriginClient => AnswerOrigin.Client,
        OriginMemory => AnswerOrigin.Memory,
        _ => null,
    };
}
