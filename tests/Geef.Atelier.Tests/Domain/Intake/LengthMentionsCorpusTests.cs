using Geef.Atelier.Core.Domain.Intake;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary>
/// Review round 2 of stage 2: a length in pages, sentences, paragraphs, lines or a tweet only counts when it clearly belongs to
/// the writing task. A small synthetic corpus (German and English) measures hits and false alarms of <see cref="LengthMentions.InRequest"/>.
/// </summary>
public sealed class LengthMentionsCorpusTests(ITestOutputHelper output)
{
    public static readonly string[] Named =
    [
        "Schreib eine halbe Seite über unser Sommerfest.",
        "Schreib in 3 Sätzen eine Absage an den Bewerber.",
        "Schreib zwei Absätze über das neue Büro.",
        "Schreib eine kurze Seite über die neuen Öffnungszeiten.",
        "Formuliere drei kurze Sätze zum Abschied von Frau Lenz.",
        "Fass das Protokoll in ein paar Sätzen zusammen.",
        "Bitte eine Absage in zwei Sätzen an den Lieferanten.",
        "Erstelle eine einseitige Zusammenfassung des Berichts.",
        "Schreib mir einen Einzeiler für die Geburtstagskarte.",
        "Verfasse höchstens zwei Seiten zur Projektidee.",
        "Ich brauche einen Absatz über unser Team für die Website.",
        "Schreib einen Tweet zur Eröffnung unseres Cafés.",
        "schreib mal 3 sätze an oma wegen sonntag",
        "Write a one-page summary of the board meeting.",
        "Write two paragraphs about our new office.",
        "Write a tweet about our summer party.",
        "Write half a page on the new opening hours.",
        "Write a paragraph introducing our new colleague.",
        "Summarize the report in three sentences.",
        "Draft a short thank-you note in a few sentences.",
        "Please write no more than two pages on the project.",
        "I need a single paragraph about our bakery for the flyer.",
        "Give me a one-pager on our pricing.",
        "write 2 short paragraphs for the club newsletter pls",
    ];

    public static readonly string[] NotNamed =
    [
        "Es gibt Streit im Verein: die eine Seite will zahlen, die andere nicht. Schreib einen Schlichtungsbrief.",
        "Schreib eine Stellungnahme wegen der zwei Seiten des Streits um den Parkplatz.",
        "Im Buch fehlen zehn Seiten. Schreib eine Reklamation an den Verlag.",
        "Seit drei Zeilen fällt der Zug aus, schreib eine Beschwerde an die Bahn.",
        "Er hat in einem Satz alles kaputt gemacht. Schreib eine Entschuldigung an das Team.",
        "Antworte auf den Tweet eines Kunden mit einer ausführlichen E-Mail.",
        "Schreib eine Seitenleiste für die Website.",
        "Schreib einen Brief an den Vermieter, weil zwei Seiten der Nebenkostenabrechnung fehlen.",
        "Der Bericht ist einseitig. Schreib eine Gegendarstellung.",
        "Schreib eine Einladung zum Sommerfest.",
        "Formuliere eine Mail an den Kunden, der drei Seiten Feedback geschickt hat.",
        "Unser Flyer hat zwei Seiten. Schreib einen Text für die Rückseite.",
        "We lost two pages of the signed contract. Write a letter to the landlord.",
        "My son wrote a tweet that got him suspended. Write a letter to the principal.",
        "Write a landing page text for our bakery.",
        "Write a complaint: the manual is missing ten pages.",
        "Write a reply to the customer who sent us three pages of questions.",
        "Write a note to the team about the two sides of the argument.",
        "Write an email to the printer: lines 3 and 4 of the flyer are wrong.",
        "Write a speech for my sister's wedding.",
        "Write a thank-you note to the agency; their pitch had a page on our history.",
        "In one sentence he insulted the whole team. Write an apology.",
        "Reply to a tweet from a customer with a friendly email.",
        "The contract runs to forty pages. Write a short summary for the board.",
    ];

    public static TheoryData<string> NamedData() => [.. Named];

    public static TheoryData<string> NotNamedData() => [.. NotNamed];

    [Theory]
    [MemberData(nameof(NamedData))]
    public void A_length_that_belongs_to_the_writing_task_is_found(string request) =>
        Assert.True(LengthMentions.InRequest(request), request);

    [Theory]
    [MemberData(nameof(NotNamedData))]
    public void A_unit_in_another_meaning_is_no_length(string request) =>
        Assert.False(LengthMentions.InRequest(request), request);

    [Fact]
    public void The_corpus_has_no_false_alarm_and_finds_every_named_length()
    {
        var hits = Named.Count(LengthMentions.InRequest);
        var falseAlarms = NotNamed.Count(LengthMentions.InRequest);
        output.WriteLine($"Found {hits} of {Named.Length} named lengths, {falseAlarms} false alarms in {NotNamed.Length} requests without one.");

        Assert.Equal((Named.Length, 0), (hits, falseAlarms));
    }
}
