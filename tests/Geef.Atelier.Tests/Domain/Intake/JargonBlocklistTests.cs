using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

public sealed class JargonBlocklistTests
{
    [Theory]
    [InlineData("Wer ist die Zielgruppe?")]
    [InlineData("Wer sind die Zielgruppen?")]
    [InlineData("Welche TONALITÄT passt?")]
    [InlineData("Soll ein Call to Action rein?")]
    [InlineData("Soll ein call  to action rein?")]
    [InlineData("Which AI model should write it?")]
    [InlineData("Welches KI-Modell?")]
    [InlineData("Welches KI Modell?")]
    [InlineData("Is SEO important?")]
    [InlineData("Brauchst du eine Vorlage?")]
    [InlineData("Shall I use your prompts?")]
    [InlineData("Who are the stakeholders?")]
    [InlineData("Run it again?")]
    public void Finds_blocked_terms_on_word_boundaries(string text) => Assert.True(JargonBlocklist.Contains(text));

    [Theory]
    [InlineData("Wer soll die Einladung bekommen?")]
    [InlineData("Soll es ein Modelleisenbahn-Verein sein?")]
    [InlineData("Brauchst du einen Prompter?")]
    [InlineData("Is it for the runners club?")]
    [InlineData("Who reads it?")]
    [InlineData("")]
    public void Ignores_words_that_only_contain_a_term(string text) => Assert.False(JargonBlocklist.Contains(text));

    [Fact]
    public void Every_term_is_found_in_a_sentence()
    {
        foreach (var term in JargonBlocklist.Terms)
            Assert.True(JargonBlocklist.Contains($"Frage zu {term} hier?"), term);
    }

    [Theory]
    [InlineData("egal")]
    [InlineData("Egal.")]
    [InlineData("Other")]
    [InlineData("Other (please say)")]
    [InlineData("Don’t know")]
    [InlineData("Etwas anderes …")]
    [InlineData("  You decide! ")]
    [InlineData("Weiß nicht")]
    public void Recognises_filler_options(string option) => Assert.True(JargonBlocklist.IsFillerOption(option));

    [Theory]
    [InlineData("Anderes Datum")]
    [InlineData("Nur das Team")]
    [InlineData("Others in the team")]
    [InlineData("")]
    public void Real_answers_are_no_filler(string option) => Assert.False(JargonBlocklist.IsFillerOption(option));

    [Fact]
    public void The_lists_are_complete()
    {
        Assert.Equal(35, JargonBlocklist.Terms.Count);
        Assert.Contains("you decide", JargonBlocklist.FillerOptions);
        Assert.Contains("sonstiges", JargonBlocklist.FillerOptions);
    }
}
