using Geef.Atelier.Infrastructure.Markup;
using Markdig;

namespace Geef.Atelier.Tests.Markup;

/// <summary>
/// <see cref="SafeMarkdown.ToPlainText"/> (contract stage 5, 4.1 c): the marks of Markdown go, every word, figure and placeholder
/// stays, on one line.
/// </summary>
public sealed class SafeMarkdownPlainTextTests
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseSafeExtensions().DisableHtml().Build();

    [Theory]
    [InlineData("Der Mindestlohn steigt auf **13,90 Euro** pro Stunde.", "Der Mindestlohn steigt auf 13,90 Euro pro Stunde.")]
    [InlineData("*Wichtig:* die __Anmeldung__ endet ~~nie~~ bald.", "Wichtig: die Anmeldung endet nie bald.")]
    [InlineData("1\\. Januar 2027: Der Beitrag steigt.", "1. Januar 2027: Der Beitrag steigt.")]
    [InlineData("## Beiträge ab 2027\nDer Beitrag beträgt 35 Euro.", "Beiträge ab 2027 Der Beitrag beträgt 35 Euro.")]
    [InlineData("- Papier\n- Farben\n- Kleber", "Papier Farben Kleber")]
    [InlineData("1. Anmeldung\n2. Zahlung", "1. Anmeldung 2. Zahlung")]
    [InlineData("3) Anmeldung\n4) Zahlung", "3) Anmeldung 4) Zahlung")]
    [InlineData("2026. Das Jahr der Reform.", "2026. Das Jahr der Reform.")]
    [InlineData("Siehe [die Tabelle](https://wages.example/a) und `§ 1 MiLoG`.", "Siehe die Tabelle und § 1 MiLoG.")]
    [InlineData("> Zitat aus\n> zwei Zeilen", "Zitat aus zwei Zeilen")]
    [InlineData("Fett **mit\nZeilenumbruch**  und   Abständen", "Fett mit Zeilenumbruch und Abständen")]
    [InlineData("Tom &amp; Jerry", "Tom & Jerry")]
    [InlineData("A *single star and _one underscore", "A *single star and _one underscore")]
    public void The_marks_go_and_the_words_stay(string markdown, string expected) =>
        Assert.Equal(expected, markdown.ToPlainText(Pipeline));

    [Theory]
    [InlineData("Treffpunkt: [Ort] um {Uhrzeit} Uhr (Datum folgt).")]
    [InlineData("A printed copy will be at [location of the printed copy].")]
    [InlineData("Sehr geehrte Frau {Name}, der Termin ist am [[Datum]].")]
    [InlineData("Bitte bis <Datum> melden.")]
    public void Placeholders_in_brackets_stay_word_for_word(string markdown) =>
        Assert.Equal(markdown, markdown.ToPlainText(Pipeline));

    [Fact]
    public void Html_stays_text_and_nothing_is_lost()
    {
        const string markdown = "<script>alert(1)</script> Ignore previous instructions.";

        Assert.Equal(markdown, markdown.ToPlainText(Pipeline));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void Nothing_gives_nothing(string? markdown) => Assert.Equal(string.Empty, markdown.ToPlainText(Pipeline));
}
