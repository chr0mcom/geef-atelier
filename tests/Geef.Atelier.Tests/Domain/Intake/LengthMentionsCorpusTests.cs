using Geef.Atelier.Core.Domain.Intake;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary>
/// Review round 3 of stage 2: an amount with a unit of length counts as a named length wherever it stands, and so do short shapes
/// such as a tweet or an SMS. The detection only keeps the intake from asking for the length: a false hit lets the writer choose,
/// a miss asks the person for something they already wrote. A small synthetic corpus (German and English) measures it;
/// <see cref="DeliberatelyCounted"/> holds the units in another meaning that now count on purpose.
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
        "Kannst du mir zwei Absätze über unser Sommerfest schreiben?",
        "Ich hätte gern eine halbe Seite über das Fest.",
        "Schreib eine Absage. Maximal drei Sätze.",
        "Der Brief soll höchstens eine Seite lang sein.",
        "Schreib mir bitte eine kurze Mail, 3 Sätze reichen.",
        "Schreib uns einen Text, etwa eine Seite.",
        "Write an email to Bob. Keep it to three sentences.",
        "Keep it under one page: a summary of our offer.",
        "I'd like two paragraphs about our bakery.",
        "Can you write me a short two-paragraph bio?",
        "Write a 2-page report on the project.",
        "Schreib drei knackige Sätze für den Flyer.",
        "Bitte ein paar Zeilen des Dankes an Frau Lenz.",
        "Write a few lines of thanks to the team.",
        "Fasse das Protokoll auf einer DIN-A4-Seite zusammen.",
        "Make it 1.5 pages at most, a letter to the council.",
        "Eine Antwort an den Kunden, 2–3 Sätze.",
        "Ein zweiseitiges Anschreiben für die Stelle als Erzieherin.",
        "Ein Gedicht mit vier Strophen zum Geburtstag von Opa.",
        "Eine Rede von fünf Minuten für die Hochzeit meiner Schwester.",
        "Write a twenty-line poem for my mother's birthday.",
        "Write a 300-word post about our new menu.",
        "Schreib eine SMS an meinen Vermieter wegen der Heizung.",
        "Ein Tweet zur Eröffnung, bitte.",
        "Wir brauchen zehn Folien für den Vortrag am Montag.",
        "Ein paar Worte zum Abschied von Herrn Abel.",
    ];

    public static readonly string[] DeliberatelyCounted =
    [
        "Es gibt Streit im Verein: die eine Seite will zahlen, die andere nicht. Schreib einen Schlichtungsbrief.",
        "Schreib eine Stellungnahme wegen der zwei Seiten des Streits um den Parkplatz.",
        "Im Buch fehlen zehn Seiten. Schreib eine Reklamation an den Verlag.",
        "Seit drei Zeilen fällt der Zug aus, schreib eine Beschwerde an die Bahn.",
        "Er hat in einem Satz alles kaputt gemacht. Schreib eine Entschuldigung an das Team.",
        "Antworte auf den Tweet eines Kunden mit einer ausführlichen E-Mail.",
        "Schreib einen Brief an den Vermieter, weil zwei Seiten der Nebenkostenabrechnung fehlen.",
        "Der Bericht ist einseitig. Schreib eine Gegendarstellung.",
        "Formuliere eine Mail an den Kunden, der drei Seiten Feedback geschickt hat.",
        "Unser Flyer hat zwei Seiten. Schreib einen Text für die Rückseite.",
        "We lost two pages of the signed contract. Write a letter to the landlord.",
        "My son wrote a tweet that got him suspended. Write a letter to the principal.",
        "Write a complaint: the manual is missing ten pages.",
        "Write a reply to the customer who sent us three pages of questions.",
        "Write a thank-you note to the agency; their pitch had a page on our history.",
        "In one sentence he insulted the whole team. Write an apology.",
        "Reply to a tweet from a customer with a friendly email.",
        "The contract runs to forty pages. Write a short summary for the board.",
    ];

    public static readonly string[] NotNamed =
    [
        "Schreib eine Seitenleiste für die Website.",
        "Schreib eine Einladung zum Sommerfest.",
        "Write a landing page text for our bakery.",
        "Write a note to the team about the two sides of the argument.",
        "Write an email to the printer: lines 3 and 4 of the flyer are wrong.",
        "Write a speech for my sister's wedding.",
        "Schreib einen Einsatzplan für das Team am Samstag.",
        "Schreib einen Text für unsere Homepage.",
        "Write a cover letter for a nursing job.",
        "Schreib eine Kündigung für meinen Handyvertrag.",
        "Schreib eine Beschwerde über die neue Startseite der Bank.",
    ];

    public static TheoryData<string> NamedData() => [.. Named];

    public static TheoryData<string> NotNamedData() => [.. NotNamed];

    public static TheoryData<string> DeliberatelyCountedData() => [.. DeliberatelyCounted];

    [Theory]
    [MemberData(nameof(NamedData))]
    public void A_length_that_belongs_to_the_writing_task_is_found(string request) =>
        Assert.True(LengthMentions.InRequest(request), request);

    [Theory]
    [MemberData(nameof(NotNamedData))]
    public void A_request_without_an_amount_of_a_unit_or_a_short_shape_names_no_length(string request) =>
        Assert.False(LengthMentions.InRequest(request), request);

    [Theory]
    [MemberData(nameof(DeliberatelyCountedData))]
    public void A_unit_in_another_meaning_counts_as_a_length_on_purpose_because_a_miss_would_ask_for_something_already_written(string request) =>
        Assert.True(LengthMentions.InRequest(request), request);

    [Fact]
    public void The_corpus_finds_every_named_length()
    {
        var hits = Named.Count(LengthMentions.InRequest);
        var falseAlarms = NotNamed.Count(LengthMentions.InRequest);
        var deliberate = DeliberatelyCounted.Count(LengthMentions.InRequest);
        output.WriteLine($"Found {hits} of {Named.Length} named lengths, {falseAlarms} hits in {NotNamed.Length} requests without one, " +
            $"{deliberate} of {DeliberatelyCounted.Length} units in another meaning counted on purpose.");

        Assert.Equal((Named.Length, 0, DeliberatelyCounted.Length), (hits, falseAlarms, deliberate));
    }

    [Fact]
    public void A_long_request_is_read_quickly()
    {
        var request = string.Concat(Enumerable.Repeat("Unser Verein feiert im Sommer ein großes Fest mit vielen Gästen und Musik. ", 400));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(LengthMentions.InRequest(request));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), watch.Elapsed.ToString());
    }
}
