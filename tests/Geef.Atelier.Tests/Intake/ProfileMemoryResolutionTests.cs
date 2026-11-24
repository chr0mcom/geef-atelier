using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Persistence.Profiles;

namespace Geef.Atelier.Tests.Intake;

public sealed class ProfileMemoryResolutionTests
{
    private sealed class Profiles(UserProfileEntity? profile) : IUserProfileRepository
    {
        public List<string> Asked { get; } = [];
        public Task<UserProfileEntity?> GetAsync(string username, CancellationToken ct = default)
        {
            Asked.Add(username);
            return Task.FromResult(profile);
        }
        public Task UpsertAsync(UserProfileEntity profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string username, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static UserProfileEntity Profile(Formality formality = Formality.Unset) => new()
    {
        Username = "eva", DisplayName = " Eva Muster ", Role = "Teamleitung", Organization = null, Formality = formality,
        Language = "de", Signature = "Viele Grüße\nEva", NoGos = "Keine Gender-Doppelformen", FreeText = "Ich schreibe gern kurz.",
        CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void The_profile_answers_language_sender_signature_and_formality()
    {
        var resolution = new ProfileMemoryResolution(new Profiles(null));

        var known = resolution.Resolve(new MemoryFacts(Profile(Formality.Formal)), "Einladung");

        Assert.Equal(
            new Dictionary<GapTopic, MemoryAnswer>
            {
                [GapTopic.Language] = new(GapTopic.Language, "de", "profile"),
                [GapTopic.Sender] = new(GapTopic.Sender, "Eva Muster, Teamleitung", "profile"),
                [GapTopic.Signature] = new(GapTopic.Signature, "Viele Grüße\nEva", "profile"),
                [GapTopic.Formality] = new(GapTopic.Formality, "Formal", "profile"),
            },
            known);
    }

    [Fact]
    public void Empty_fields_answer_nothing()
    {
        var empty = Profile() with { DisplayName = " ", Role = null, Organization = "", Language = null, Signature = null };

        Assert.Empty(new ProfileMemoryResolution(new Profiles(null)).Resolve(new MemoryFacts(empty), null));
        Assert.Empty(new ProfileMemoryResolution(new Profiles(null)).Resolve(new MemoryFacts(null), null));
    }

    [Fact]
    public void One_sender_field_is_enough()
    {
        var known = new ProfileMemoryResolution(new Profiles(null))
            .Resolve(new MemoryFacts(Profile() with { DisplayName = null, Role = null, Organization = "Muster GmbH" }), null);

        Assert.Equal("Muster GmbH", known[GapTopic.Sender].Value);
    }

    [Fact]
    public async Task Loading_asks_only_for_the_own_name_and_nothing_without_a_name()
    {
        var profiles = new Profiles(Profile());
        var resolution = new ProfileMemoryResolution(profiles);

        Assert.NotNull((await resolution.LoadAsync("eva")).Profile);
        Assert.Null((await resolution.LoadAsync(" ")).Profile);
        Assert.Equal(["eva"], profiles.Asked);
    }
}
