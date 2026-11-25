using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
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

    /// <summary>The origin of an answer from its wire name, ignoring case and surrounding blanks; <c>null</c> for anything unknown.</summary>
    public static AnswerOrigin? ParseAnswerOrigin(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        OriginUser => AnswerOrigin.User,
        OriginClient => AnswerOrigin.Client,
        OriginMemory => AnswerOrigin.Memory,
        _ => null,
    };
}
