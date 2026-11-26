using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Infrastructure.Persistence.Profiles;
using Geef.Atelier.Tests.Persistence;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Profiles;

/// <summary>
/// <see cref="ProfileService.SavePartAsync"/> with the real repository against Postgres: it sets only the given fields of the own profile,
/// keeps every other field and the creation time, creates a profile when there is none, checks like a full save and never touches the
/// profile of anyone else.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class ProfileServicePartTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_part_creates_the_profile_when_there_is_none()
    {
        var user = NewUser();
        await using var db = fixture.NewContext();

        var view = await Service(db, T0).SavePartAsync(user, new UserProfilePart(Signature: " Viele Grüße\nErika Beispiel "));

        Assert.False(view.IsEmpty);
        Assert.Equal(new UserProfileInput(null, null, null, Formality.Unset, null, "Viele Grüße\nErika Beispiel", null, null), view.Values);
        var stored = await new UserProfileRepository(db).GetAsync(user);
        Assert.Equal((T0, T0), (stored!.CreatedAt, stored.UpdatedAt));
    }

    [Fact]
    public async Task A_part_sets_only_its_fields_and_keeps_the_rest_and_the_creation_time()
    {
        var user = NewUser();
        await using (var db = fixture.NewContext())
            await Service(db, T0).SaveAsync(user, new UserProfileInput("Alt", "Kassenwart", "Verein Beispiel e. V.", Formality.Formal, "en", "Gruß", "Keine Emojis", "Kurz halten."));

        await using (var db = fixture.NewContext())
            await Service(db, T0.AddDays(1)).SavePartAsync(user, new UserProfilePart(DisplayName: "Erika Beispiel", Language: " DE "));
        await using (var db = fixture.NewContext())
            await Service(db, T0.AddDays(2)).SavePartAsync(user, new UserProfilePart(Formality: Formality.Informal));

        await using var check = fixture.NewContext();
        var stored = await new UserProfileRepository(check).GetAsync(user);
        Assert.Equal(new UserProfileInput("Erika Beispiel", "Kassenwart", "Verein Beispiel e. V.", Formality.Informal, "de", "Gruß", "Keine Emojis", "Kurz halten."),
            (await Service(check, T0).GetAsync(user)).Values);
        Assert.Equal((T0, T0.AddDays(2)), (stored!.CreatedAt, stored.UpdatedAt));
    }

    [Fact]
    public async Task A_part_never_touches_another_profile()
    {
        var owner = NewUser();
        var other = NewUser();
        await using var db = fixture.NewContext();
        var service = Service(db, T0);
        await service.SaveAsync(other, new UserProfileInput("Bob", null, null, Formality.Formal, "en", "Best, Bob", null, null));

        await service.SavePartAsync(owner, new UserProfilePart("Anna", Formality.Informal, "de", "Viele Grüße, Anna"));

        Assert.Equal(new UserProfileInput("Bob", null, null, Formality.Formal, "en", "Best, Bob", null, null), (await service.GetAsync(other)).Values);
        Assert.Equal(new UserProfileInput("Anna", null, null, Formality.Informal, "de", "Viele Grüße, Anna", null, null), (await service.GetAsync(owner)).Values);
    }

    public static TheoryData<UserProfilePart, string> Refused() => new()
    {
        { new UserProfilePart(), ProfileService.NothingToSaveMessage },
        { new UserProfilePart(DisplayName: "  "), ProfileService.NothingToSaveMessage },
        { new UserProfilePart(Signature: ""), ProfileService.NothingToSaveMessage },
        { new UserProfilePart(Language: " "), ProfileService.NothingToSaveMessage },
        { new UserProfilePart(Language: "Deutsch"), ProfileService.LanguageMessage },
        { new UserProfilePart(Formality: Formality.Unset), ProfileService.FormalityMessage },
        { new UserProfilePart(Formality: (Formality)7), ProfileService.FormalityMessage },
        { new UserProfilePart(DisplayName: new string('x', UserProfileLimits.DisplayName + 1)), "\"Your name\" is too long (201 characters, at most 200)." },
        { new UserProfilePart(Signature: new string('x', UserProfileLimits.Signature + 1)), "\"Signature\" is too long (1,001 characters, at most 1,000)." },
        { new UserProfilePart(DisplayName: "Anna", Language: "deu"), ProfileService.LanguageMessage },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task A_part_the_profile_refuses_saves_nothing(UserProfilePart part, string message)
    {
        var user = NewUser();
        await using var db = fixture.NewContext();
        var service = Service(db, T0);
        await service.SaveAsync(user, new UserProfileInput("Bleibt", null, null, Formality.Unset, null, null, null, null));

        var ex = await Assert.ThrowsAsync<ProfileValidationException>(() => service.SavePartAsync(user, part));

        Assert.Equal(message, ex.Message);
        Assert.Equal(new UserProfileInput("Bleibt", null, null, Formality.Unset, null, null, null, null), (await service.GetAsync(user)).Values);
    }

    private static ProfileService Service(Geef.Atelier.Infrastructure.Persistence.AtelierDbContext db, DateTimeOffset now) =>
        new(new UserProfileRepository(db), new Clock(now));

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
