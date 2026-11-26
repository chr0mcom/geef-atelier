using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Persistence.Profiles;

namespace Geef.Atelier.Application.Intake;

/// <summary>Stage 2 memory: answers language, sender, signature and formality from the person's own profile.</summary>
internal sealed class ProfileMemoryResolution(IUserProfileRepository profiles) : IMemoryResolution
{
    /// <summary>Source of every answer in stage 2.</summary>
    public const string ProfileSource = "profile";

    public async Task<MemoryFacts> LoadAsync(string username, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(username)
            ? new MemoryFacts(null)
            : new MemoryFacts(await profiles.GetAsync(username, ct));

    public IReadOnlyDictionary<GapTopic, MemoryAnswer> Resolve(MemoryFacts facts, string? textType) => Answers(facts.Profile);

    /// <summary>The answers <paramref name="profile"/> gives, by topic; none without a profile. The same for every text type.</summary>
    internal static IReadOnlyDictionary<GapTopic, MemoryAnswer> Answers(UserProfileEntity? profile)
    {
        var answers = new Dictionary<GapTopic, MemoryAnswer>();
        if (profile is null)
            return answers;

        if (Value(profile.Language) is { } language)
            answers[GapTopic.Language] = new MemoryAnswer(GapTopic.Language, language, ProfileSource);

        var sender = string.Join(", ", new[] { profile.DisplayName, profile.Role, profile.Organization }.Select(Value).OfType<string>());
        if (sender.Length > 0)
            answers[GapTopic.Sender] = new MemoryAnswer(GapTopic.Sender, sender, ProfileSource);

        if (Value(profile.Signature) is { } signature)
            answers[GapTopic.Signature] = new MemoryAnswer(GapTopic.Signature, signature, ProfileSource);

        if (profile.Formality != Formality.Unset)
            answers[GapTopic.Formality] = new MemoryAnswer(GapTopic.Formality, profile.Formality.ToString(), ProfileSource);

        return answers;
    }

    private static string? Value(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
