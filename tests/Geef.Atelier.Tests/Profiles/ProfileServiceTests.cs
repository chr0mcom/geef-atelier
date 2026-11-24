using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Infrastructure.Persistence.Profiles;
using Geef.Atelier.Tests.Persistence;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Profiles;

/// <summary><see cref="ProfileService"/> with the real repository against Postgres: limits, language code, blanks, clearing and the own name only.</summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class ProfileServiceTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Without_a_profile_the_view_is_empty()
    {
        await using var db = fixture.NewContext();
        var view = await Service(db, new Clock(T0)).GetAsync(NewUser());

        Assert.True(view.IsEmpty);
        Assert.Null(view.UpdatedAt);
        Assert.Equal(new UserProfileInput(null, null, null, Formality.Unset, null, null, null, null), view.Values);
    }

    [Fact]
    public async Task Saving_trims_turns_blanks_into_null_lowercases_the_language_and_keeps_the_creation_time()
    {
        var user = NewUser();
        var time = new Clock(T0);
        await using (var db = fixture.NewContext())
        {
            var saved = await Service(db, time).SaveAsync(user, new UserProfileInput(" Anna Beispiel ", "  ", null, Formality.Formal, " DE ", "Viele Grüße\nAnna ", "", "\t"));
            Assert.Equal(new UserProfileInput("Anna Beispiel", null, null, Formality.Formal, "de", "Viele Grüße\nAnna", null, null), saved.Values);
            Assert.False(saved.IsEmpty);
            Assert.Equal(T0, saved.UpdatedAt);
        }

        time.Advance(TimeSpan.FromDays(2));
        await using (var db = fixture.NewContext())
        {
            var saved = await Service(db, time).SaveAsync(user, new UserProfileInput(null, "Teamleitung", null, Formality.Unset, null, null, null, null));
            Assert.Equal(T0.AddDays(2), saved.UpdatedAt);
        }

        await using var check = fixture.NewContext();
        var stored = await new UserProfileRepository(check).GetAsync(user);
        Assert.Equal((T0, T0.AddDays(2), "Teamleitung", (string?)null), (stored!.CreatedAt, stored.UpdatedAt, stored.Role, stored.DisplayName));
        var view = await Service(check, time).GetAsync(user);
        Assert.Equal("Teamleitung", view.Values.Role);
    }

    [Theory]
    [InlineData("Your name", 201, 0)]
    [InlineData("What you do", 201, 1)]
    [InlineData("Organization", 201, 2)]
    [InlineData("Signature", 1_001, 3)]
    [InlineData("Never do this", 2_001, 4)]
    [InlineData("Anything else we should know", 4_001, 5)]
    public async Task Every_limit_is_enforced_with_a_plain_message(string label, int length, int field)
    {
        var value = new string('x', length);
        var input = field switch
        {
            0 => Empty() with { DisplayName = value },
            1 => Empty() with { Role = value },
            2 => Empty() with { Organization = value },
            3 => Empty() with { Signature = value },
            4 => Empty() with { NoGos = value },
            _ => Empty() with { FreeText = value },
        };
        var user = NewUser();
        await using var db = fixture.NewContext();
        var service = Service(db, new Clock(T0));

        var ex = await Assert.ThrowsAsync<ProfileValidationException>(() => service.SaveAsync(user, input));

        Assert.Equal($"\"{label}\" is too long ({length:N0} characters, at most {length - 1:N0}).", ex.Message);
        Assert.True((await service.GetAsync(user)).IsEmpty);
        Assert.False((await service.SaveAsync(user, input with
        {
            DisplayName = input.DisplayName?[1..], Role = input.Role?[1..], Organization = input.Organization?[1..],
            Signature = input.Signature?[1..], NoGos = input.NoGos?[1..], FreeText = input.FreeText?[1..],
        })).IsEmpty);
    }

    [Theory]
    [InlineData("deu")]
    [InlineData("d")]
    [InlineData("de-DE")]
    [InlineData("12")]
    [InlineData("Deutsch")]
    public async Task The_language_must_be_a_two_letter_code(string language)
    {
        await using var db = fixture.NewContext();
        var ex = await Assert.ThrowsAsync<ProfileValidationException>(() =>
            Service(db, new Clock(T0)).SaveAsync(NewUser(), Empty() with { Language = language }));
        Assert.Equal(ProfileService.LanguageMessage, ex.Message);
    }

    [Fact]
    public async Task Clearing_removes_only_the_own_profile()
    {
        var owner = NewUser();
        var other = NewUser();
        await using var db = fixture.NewContext();
        var service = Service(db, new Clock(T0));
        await service.SaveAsync(owner, Empty() with { NoGos = "Keine Floskeln" });
        await service.SaveAsync(other, Empty() with { NoGos = "Keine Anglizismen" });

        await service.ClearAsync(owner);
        await service.ClearAsync(owner);

        Assert.True((await service.GetAsync(owner)).IsEmpty);
        Assert.Equal("Keine Anglizismen", (await service.GetAsync(other)).Values.NoGos);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(" "));
    }

    private static UserProfileInput Empty() => new(null, null, null, Formality.Unset, null, null, null, null);

    private static ProfileService Service(Geef.Atelier.Infrastructure.Persistence.AtelierDbContext db, TimeProvider time) =>
        new(new UserProfileRepository(db), time);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
