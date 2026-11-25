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

    [Fact]
    public void The_values_of_a_rendered_block_come_back_without_field_names_and_without_the_formality()
    {
        var profile = Profile() with
        {
            DisplayName = "Anna Beispiel",
            Role = "Teamleitung",
            Organization = "Beispiel GmbH",
            Formality = Formality.Formal,
            Language = "de",
            Signature = "Viele Grüße\nAnna",
            NoGos = "Keine Gender-Doppelformen",
            FreeText = "Name: steht auch hier\nzweite Zeile",
        };

        var values = ProfilePrompt.Values(ProfilePrompt.Render(profile));

        Assert.Equal(["Anna Beispiel", "Teamleitung", "Beispiel GmbH", "de", "Viele Grüße\nAnna", "Keine Gender-Doppelformen", "Name: steht auch hier\nzweite Zeile"],
            values.Values);
        Assert.True(values.HasFreeText);
        Assert.DoesNotContain("formally", values.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Preferred", values.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_formality_and_language_give_no_free_text_and_a_block_without_labels_keeps_every_line()
    {
        var values = ProfilePrompt.Values(ProfilePrompt.Render(Profile() with { Formality = Formality.Informal, Language = "en" }));
        Assert.Equal(["en"], values.Values);
        Assert.False(values.HasFreeText);

        Assert.Equal(["Ich heiße Oberhuber.\nAus Kassel."], ProfilePrompt.Values("Ich heiße Oberhuber.\nAus Kassel.").Values);
        Assert.Empty(ProfilePrompt.Values(null).Values);
    }

    private static UserProfileEntity Profile() => new() { Username = "user-a", CreatedAt = T0, UpdatedAt = T0 };
}
