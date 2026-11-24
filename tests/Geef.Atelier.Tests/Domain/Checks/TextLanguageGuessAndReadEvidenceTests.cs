using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Tests.Domain.Checks;

public sealed class TextLanguageGuessTests
{
    [Theory]
    [InlineData("Vielen Dank für Ihre Nachricht. Wir bestätigen gerne, dass die Reparatur der Heizung am Montag stattfindet und wir um neun Uhr bei Ihnen sind.", "de")]
    [InlineData("Thank you for your message. We are happy to confirm that the repair of the heating will take place on Monday, and we will be there at nine.", "en")]
    public void Recognises_clear_texts(string text, string language) =>
        Assert.Equal(language, TextLanguageGuess.Guess(text));

    [Theory]
    [InlineData("")]
    [InlineData("Heizung Montag neun Uhr")]
    [InlineData("Bonjour, je vous remercie de votre message et je vous confirme le rendez-vous de lundi.")]
    [InlineData("Bitte beantworte diese Mail für mich und sei dabei freundlich: Dear Mr Smith, thank you for the offer, we would like to know whether the price includes delivery and what the terms are.")]
    public void Returns_null_when_unsure(string text) =>
        Assert.Null(TextLanguageGuess.Guess(text));
}

public sealed class ReadEvidenceCheckTests
{
    [Fact]
    public void Searched_files_fall_below_the_threshold_and_read_files_pass()
    {
        var searched = ReadEvidenceCheck.Evaluate(160_000, 30_000);
        var read = ReadEvidenceCheck.Evaluate(160_000, 105_000);

        Assert.NotNull(searched);
        Assert.False(searched.ReadInFull);
        Assert.Equal(ReadEvidenceCheck.AgentBaseTokens + 84_211, searched.ExpectedInputTokens);
        Assert.NotNull(read);
        Assert.True(read.ReadInFull);
    }

    [Fact]
    public void The_threshold_is_inclusive()
    {
        var expected = ReadEvidenceCheck.AgentBaseTokens + 10_000;
        Assert.True(ReadEvidenceCheck.Evaluate(19_000, (int)(expected * 0.6))!.ReadInFull);
        Assert.False(ReadEvidenceCheck.Evaluate(19_000, (int)(expected * 0.6) - 50)!.ReadInFull);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1000, 0)]
    public void Nothing_to_judge_gives_null(int characters, int tokens) =>
        Assert.Null(ReadEvidenceCheck.Evaluate(characters, tokens));

    [Fact]
    public void English_files_use_the_english_estimate()
    {
        var english = string.Concat(Enumerable.Repeat("The report describes the work of the harbour and the people who do it. ", 20));
        var german = string.Concat(Enumerable.Repeat("Der Bericht beschreibt die Arbeit im Hafen und die Menschen, die sie tun. ", 20));

        Assert.Equal(ReadEvidenceCheck.EnglishCharactersPerToken, ReadEvidenceCheck.CharactersPerTokenFor(english));
        Assert.Equal(ReadEvidenceCheck.GermanCharactersPerToken, ReadEvidenceCheck.CharactersPerTokenFor(german));
        Assert.Equal(ReadEvidenceCheck.GermanCharactersPerToken, ReadEvidenceCheck.CharactersPerTokenFor(null));
    }
}
