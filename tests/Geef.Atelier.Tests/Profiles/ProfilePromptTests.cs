using Geef.Atelier.Core.Domain.Profiles;

namespace Geef.Atelier.Tests.Profiles;

/// <summary><see cref="ProfilePrompt"/>: empty detection and the fixed field order of the rendered block.</summary>
public sealed class ProfilePromptTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void No_profile_or_only_blanks_is_empty()
    {
        Assert.True(ProfilePrompt.IsEmpty(null));
        Assert.True(ProfilePrompt.IsEmpty(Profile()));
        Assert.True(ProfilePrompt.IsEmpty(Profile() with { DisplayName = "  ", FreeText = "" }));
        Assert.Null(ProfilePrompt.Render(null));
        Assert.Null(ProfilePrompt.Render(Profile() with { Signature = " " }));
    }

    [Fact]
    public void A_single_field_or_the_formality_makes_it_non_empty()
    {
        Assert.False(ProfilePrompt.IsEmpty(Profile() with { NoGos = "Keine Gender-Doppelformen" }));
        Assert.False(ProfilePrompt.IsEmpty(Profile() with { Formality = Formality.Informal }));
        Assert.Equal("Address the readers: informally", ProfilePrompt.Render(Profile() with { Formality = Formality.Informal }));
    }

    [Fact]
    public void All_fields_render_in_fixed_order_with_english_labels()
    {
        var profile = Profile() with
        {
            DisplayName = " Anna Beispiel ",
            Role = "Teamleitung",
            Organization = "Beispiel GmbH",
            Formality = Formality.Formal,
            Language = "de",
            Signature = "Viele Grüße\nAnna",
            NoGos = "Keine Gender-Doppelformen",
            FreeText = "Ich schreibe oft an Kunden.",
        };

        Assert.Equal(
            """
            Name: Anna Beispiel
            Role: Teamleitung
            Organization: Beispiel GmbH
            Address the readers: formally
            Preferred language (when the order names none): de
            Signature:
            Viele Grüße
            Anna
            Never do this: Keine Gender-Doppelformen
            Anything else: Ich schreibe oft an Kunden.
            """.ReplaceLineEndings("\n"),
            ProfilePrompt.Render(profile));
    }

    private static UserProfileEntity Profile() => new() { Username = "user-a", CreatedAt = T0, UpdatedAt = T0 };
}
