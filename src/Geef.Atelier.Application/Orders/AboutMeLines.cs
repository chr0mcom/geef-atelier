using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Application.Orders;

/// <summary>Sentences about About me on a result ("Tell Atelier once", "From About me"), the same on the web and in MCP. Plain English.</summary>
public static class AboutMeLines
{
    /// <summary>
    /// Heading of the offer under <see cref="TellOnceMode.On"/>. True by code: once saved, the reading of the next request takes the
    /// topic from About me and never assumes it or asks for it again.
    /// </summary>
    public const string TellOnce = "Tell Atelier once – it will not leave this open again";

    /// <summary>Heading of the offer under <see cref="TellOnceMode.WithoutPromise"/>.</summary>
    public const string TellOnceWithoutPromise = "Save it under About me for your next texts";

    /// <summary>Line under the heading of the offer.</summary>
    public const string TellOnceLead = "This text had to guess or leave this open because About me has nothing on it yet.";

    /// <summary>
    /// Shown after saving one field. Only the next requests get it: this text, and any change asked for to it, keeps the details it was
    /// written with (a change reuses the briefing of the version it changes).
    /// </summary>
    public const string Saved = "Saved under About me. Your next requests get it. This text, and any change you ask for to it, keeps the details it was written with.";

    /// <summary>Shown instead of <see cref="Saved"/> when the field was set under About me in the meantime; nothing is changed then.</summary>
    public const string AlreadySaved = "Already saved under About me, so nothing was changed. You can change it under About me.";

    /// <summary>Lead of the topics of About me that the writer got with this request (sent, not necessarily used).</summary>
    public const string FromAboutMeLead = "With this request the writer got from About me: ";

    /// <summary>End of the line with the topics the writer got from About me.</summary>
    public const string ChangeThere = "You can change or delete it under About me; that counts for your next requests.";

    /// <summary>Label of the save button.</summary>
    public const string SaveButton = "Save";

    /// <summary>Error when saving failed for a reason on our side.</summary>
    public const string SaveFailed = "Something went wrong on our side. Your entry is still here. Try again in a moment.";

    /// <summary>The heading of the offer for <paramref name="mode"/>; <c>null</c> when the offer is off.</summary>
    public static string? Heading(this TellOnceMode mode) => mode switch
    {
        TellOnceMode.On => TellOnce,
        TellOnceMode.WithoutPromise => TellOnceWithoutPromise,
        _ => null,
    };

    /// <summary>Label of the field to fill in for <paramref name="topic"/>, as on the page About me.</summary>
    public static string FieldLabel(this GapTopic topic) => topic switch
    {
        GapTopic.Sender => "Your name",
        GapTopic.Signature => "How you sign off",
        GapTopic.Formality => "How should readers be addressed?",
        GapTopic.Language => "Language of your texts",
        _ => topic.Label(),
    };

    /// <summary>Name of <paramref name="topic"/> in the line <see cref="FromAboutMeLead"/>.</summary>
    public static string FromAboutMeName(this GapTopic topic) => topic switch
    {
        GapTopic.Sender => "who you are",
        GapTopic.Signature => "how you sign off",
        GapTopic.Formality => "how readers are addressed",
        GapTopic.Language => "the language of your texts",
        _ => topic.Label().ToLowerInvariant(),
    };

    /// <summary>The line about the topics the writer got from About me, for example "With this request the writer got from About me: who you are, how you sign off. You can change …".</summary>
    public static string FromAboutMeLine(IReadOnlyList<GapTopic> topics)
    {
        ArgumentNullException.ThrowIfNull(topics);
        return FromAboutMeLead + string.Join(", ", topics.Select(t => t.FromAboutMeName())) + ". " + ChangeThere;
    }
}
