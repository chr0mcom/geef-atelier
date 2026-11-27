namespace Geef.Atelier.Core.Domain.Orders;

/// <summary>
/// A quick button for a change of a delivered text. <see cref="Longer"/> is not offered: a caller that still asks for it is refused with
/// the request to say what should be added.
/// </summary>
public enum RevisionQuick { Shorter = 0, Longer = 1, MoreFormal = 2, Friendlier = 3, Simpler = 4 }

/// <summary>The fixed sentence a quick button sends to the writer and the name the person sees on it.</summary>
public static class RevisionQuickText
{
    /// <summary>The buttons offered on the web page and in MCP, in the order of the buttons; every value but <see cref="RevisionQuick.Longer"/>.</summary>
    public static IReadOnlyList<RevisionQuick> Offered { get; } = [RevisionQuick.Shorter, RevisionQuick.MoreFormal, RevisionQuick.Friendlier, RevisionQuick.Simpler];

    /// <summary>True for a button that is offered; false for <see cref="RevisionQuick.Longer"/> and unknown values.</summary>
    public static bool IsOffered(this RevisionQuick q) => Offered.Contains(q);

    /// <summary>The sentence for the writer, in English.</summary>
    public static string Instruction(this RevisionQuick q) => q switch
    {
        RevisionQuick.Shorter => "Make the text about a third shorter.",
        RevisionQuick.Longer => "Make the text about a third longer without adding new facts.",
        RevisionQuick.MoreFormal => "Make the tone more formal.",
        RevisionQuick.Friendlier => "Make the tone warmer and friendlier.",
        RevisionQuick.Simpler => "Use simpler words and shorter sentences.",
        _ => throw new ArgumentOutOfRangeException(nameof(q), q, null),
    };

    /// <summary>The name of the button.</summary>
    public static string Label(this RevisionQuick q) => q switch
    {
        RevisionQuick.Shorter => "Shorter",
        RevisionQuick.Longer => "Longer",
        RevisionQuick.MoreFormal => "More formal",
        RevisionQuick.Friendlier => "Friendlier",
        RevisionQuick.Simpler => "Simpler words",
        _ => throw new ArgumentOutOfRangeException(nameof(q), q, null),
    };
}
