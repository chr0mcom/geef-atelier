using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Application.Orders;

/// <summary>
/// The block of the briefing that carries the changes the person asked for: placed before the block with the About me details, one line
/// per version, older changes kept, newest last.
/// </summary>
public static class RevisionBrief
{
    /// <summary>Heading of the block, word for word.</summary>
    public const string Heading = "Change requested by the person (newest last; where changes differ, the newest wins):";

    /// <summary>One line of the block: <c>{0}</c> is the version, <c>{1}</c> the change.</summary>
    public const string LineFormat = "- (version {0}) {1}";

    /// <summary>
    /// The briefing of version <paramref name="version"/>: <paramref name="baseBriefing"/> (the briefing of the shown version) with the
    /// line for <paramref name="change"/> added to the block, which is created before the block with the About me details when missing.
    /// </summary>
    public static string Compose(string baseBriefing, string change, int version) => throw new NotImplementedException();

    /// <summary>The change as sent to the writer: the trimmed words of the person and the sentences of the quick buttons; empty when there is neither.</summary>
    public static string Change(string? text, IReadOnlyList<RevisionQuick> quick) => throw new NotImplementedException();
}
