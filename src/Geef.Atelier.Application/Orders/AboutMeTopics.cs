using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;

namespace Geef.Atelier.Application.Orders;

/// <summary>
/// Pure: which of the four topics About me can answer (sender, signature, form of address, language) a result had to guess or leave
/// open although the person could say it once, and which of them came from About me. The topics come only from the reading of the request
/// (<see cref="IntakeRecord"/>) and the stored briefing, never from the wording of the text.
/// </summary>
public static class AboutMeTopics
{
    /// <summary>The topics About me answers, in the order they are shown.</summary>
    public static IReadOnlyList<GapTopic> Topics { get; } = [GapTopic.Sender, GapTopic.Signature, GapTopic.Formality, GapTopic.Language];

    /// <summary>The topics <paramref name="profile"/> answers, as the intake answers them before writing; none without a profile.</summary>
    public static IReadOnlySet<GapTopic> Known(UserProfileEntity? profile) => ProfileMemoryResolution.Answers(profile).Keys.ToHashSet();

    /// <summary>
    /// The topics of <see cref="Topics"/> that the intake of the order assumed or left open – an assumption nobody corrected, or a question
    /// the person left open (<see cref="OpenQuestions.IsLeftOpen"/>) – and that <paramref name="profile"/> does not answer, in the order of
    /// <see cref="Topics"/>. Empty without a record (the request was not read).
    /// </summary>
    public static IReadOnlyList<GapTopic> Missing(IntakeRecord? record, IReadOnlyList<OrderAnswer> answers, UserProfileEntity? profile)
    {
        ArgumentNullException.ThrowIfNull(answers);
        if (record is null)
            return [];
        var byQuestion = answers.GroupBy(a => a.QuestionId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var corrected = answers.Where(a => !string.IsNullOrWhiteSpace(a.FreeText)).Select(a => a.QuestionId).ToHashSet(StringComparer.Ordinal);
        var open = (record.Assumptions ?? []).Where(a => !corrected.Contains(a.Id)).Select(a => a.Topic)
            .Concat((record.Questions ?? []).Where(q => q.IsLeftOpen(byQuestion)).Select(q => q.Slot))
            .ToHashSet();
        var known = Known(profile);
        return Topics.Where(t => open.Contains(t) && !known.Contains(t)).ToList();
    }

    /// <summary>
    /// The topics of <see cref="Topics"/> that came from About me for a version: the fields of the profile block in its briefing
    /// (name, role or organization count as the sender) and the answers the intake took from the profile, in the order of
    /// <see cref="Topics"/>.
    /// </summary>
    public static IReadOnlyList<GapTopic> FromAboutMe(string? briefing, IntakeRecord? record)
    {
        var topics = ProfilePrompt.Fields(BriefingSections.Parse(briefing ?? string.Empty).Profile).Select(Topic).OfType<GapTopic>()
            .Concat((record?.FromMemory ?? []).Select(m => m.Topic))
            .ToHashSet();
        return Topics.Where(topics.Contains).ToList();
    }

    private static GapTopic? Topic(string label) => label switch
    {
        ProfilePrompt.NameLabel or ProfilePrompt.RoleLabel or ProfilePrompt.OrganizationLabel => GapTopic.Sender,
        ProfilePrompt.SignatureLabel => GapTopic.Signature,
        ProfilePrompt.FormalityLabel => GapTopic.Formality,
        ProfilePrompt.LanguageLabel => GapTopic.Language,
        _ => null,
    };
}
