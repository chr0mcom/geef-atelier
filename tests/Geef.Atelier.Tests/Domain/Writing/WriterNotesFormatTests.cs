using System.Text;
using System.Text.Json;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Domain.Writing;

public sealed class WriterNotesFormatTests
{
    private const string Letter = "Sehr geehrte Eltern,\n\nder Elternabend findet am Donnerstag statt.\n\nViele Grüße\nIhre Klassenleitung";

    private const string ValidBlock = """
        <!-- atelier-notes
        {"assumptions": ["Anrede an alle Eltern gewählt."], "open_points": ["Uhrzeit fehlt, Platzhalter gesetzt."], "quotes": [{"source": "S1", "quote": "Elternabend am Donnerstag"}]}
        -->
        """;

    private static readonly string[] Remnants = ["atelier-notes", "<!--", "-->", "{\"assumptions\"", "\"open_points\""];

    private static void AssertNoRemnant(string text)
    {
        foreach (var remnant in Remnants)
            Assert.DoesNotContain(remnant, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Valid_block_is_split_off_and_read()
    {
        var split = WriterNotesFormat.Split(Letter + "\n\n" + ValidBlock + "\n");

        Assert.Equal(WriterNotesStatus.Ok, split.Status);
        Assert.Equal(Letter, split.Text);
        Assert.Equal(["Anrede an alle Eltern gewählt."], split.Notes.Assumptions);
        Assert.Equal(["Uhrzeit fehlt, Platzhalter gesetzt."], split.Notes.OpenPoints);
        Assert.Equal([new WriterQuote("S1", "Elternabend am Donnerstag")], split.Notes.Quotes);
    }

    [Fact]
    public void Several_markers_the_last_one_counts_and_earlier_blocks_leave_the_text()
    {
        const string first = "<!-- atelier-notes\n{\"assumptions\": [\"alt\"], \"open_points\": [], \"quotes\": []}\n-->";
        const string second = "<!-- atelier-notes\n{\"assumptions\": [\"neu\"], \"open_points\": [], \"quotes\": []}\n-->";
        var split = WriterNotesFormat.Split("Absatz eins.\n\n" + first + "\n\nAbsatz zwei.\n\n" + second);

        Assert.Equal(WriterNotesStatus.Ok, split.Status);
        Assert.Equal(["neu"], split.Notes.Assumptions);
        Assert.Equal("Absatz eins.\n\nAbsatz zwei.", split.Text);
    }

    [Fact]
    public void A_marker_inside_a_line_counts_only_when_none_starts_a_line()
    {
        var inline = WriterNotesFormat.Split("Text endet hier. <!-- atelier-notes {\"assumptions\": [\"a\"]} -->");
        Assert.Equal("Text endet hier.", inline.Text);
        Assert.Equal(WriterNotesStatus.Ok, inline.Status);

        var both = WriterNotesFormat.Split("Erwähnt <!-- atelier-notes im Satz.\n<!-- atelier-notes\n{\"assumptions\": [\"b\"]}\n-->");
        Assert.Equal("Erwähnt", both.Text);
        Assert.Equal(["b"], both.Notes.Assumptions);
    }

    [Theory]
    [InlineData("<!-- atelier-notes\n{\"assumptions\": [\"a\", }\n-->")]
    [InlineData("<!-- atelier-notes\nkein JSON\n-->")]
    [InlineData("<!-- atelier-notes\n[\"liste\"]\n-->")]
    [InlineData("<!-- atelier-notes")]
    public void Broken_json_is_unreadable_and_leaves_no_rest(string block)
    {
        var split = WriterNotesFormat.Split(Letter + "\n\n" + block);

        Assert.Equal(WriterNotesStatus.Unreadable, split.Status);
        Assert.Equal(Letter, split.Text);
        Assert.Same(WriterNotes.Empty, split.Notes);
        AssertNoRemnant(split.Text);
    }

    [Fact]
    public void Missing_end_marker_still_reads_the_block()
    {
        var split = WriterNotesFormat.Split(Letter + "\n<!-- atelier-notes\n{\"assumptions\": [\"x\"], \"open_points\": [], \"quotes\": []}");

        Assert.Equal(WriterNotesStatus.Ok, split.Status);
        Assert.Equal(Letter, split.Text);
        Assert.Equal(["x"], split.Notes.Assumptions);
    }

    [Fact]
    public void Text_after_the_block_is_removed_as_well()
    {
        var split = WriterNotesFormat.Split(Letter + "\n" + ValidBlock + "\nIch hoffe, das hilft!");

        Assert.Equal(Letter, split.Text);
        Assert.Equal(WriterNotesStatus.Ok, split.Status);
    }

    [Theory]
    [InlineData("<!--atelier-notes\n{\"assumptions\": [\"x\"]}\n-->")]
    [InlineData("<!-- ATELIER-NOTES\n{\"assumptions\": [\"x\"]}\n-->")]
    [InlineData("<!-- atelier_notes {\"assumptions\": [\"x\"]} -->")]
    [InlineData("   <!-- atelier-notes\n```json\n{\"assumptions\": [\"x\"]}\n```\n-->")]
    public void Spelling_variants_of_the_marker_are_split(string block)
    {
        var split = WriterNotesFormat.Split(Letter + "\n" + block);

        Assert.Equal(Letter, split.Text);
        Assert.Equal(["x"], split.Notes.Assumptions);
    }

    [Fact]
    public void Block_wrapped_in_a_code_fence_leaves_no_open_fence()
    {
        var split = WriterNotesFormat.Split(Letter + "\n\n```html\n" + ValidBlock + "\n```");

        Assert.Equal(Letter, split.Text);
        Assert.Equal(WriterNotesStatus.Ok, split.Status);
    }

    [Fact]
    public void Final_json_code_block_without_marker_is_taken_as_the_block()
    {
        var split = WriterNotesFormat.Split(Letter + "\n\n```json\n{\"assumptions\": [\"y\"], \"open_points\": []}\n```\n");

        Assert.Equal(WriterNotesStatus.Ok, split.Status);
        Assert.Equal(Letter, split.Text);
        Assert.Equal(["y"], split.Notes.Assumptions);
    }

    [Fact]
    public void Final_json_code_block_without_known_key_stays_text()
    {
        var raw = Letter + "\n\n```json\n{\"name\": \"Beispiel\"}\n```";

        var split = WriterNotesFormat.Split(raw);

        Assert.Equal(WriterNotesStatus.Absent, split.Status);
        Assert.Equal(raw, split.Text);
    }

    [Fact]
    public void Final_bare_json_with_only_block_keys_is_taken_as_the_block()
    {
        var split = WriterNotesFormat.Split(Letter + "\n\n{\"assumptions\": [\"z\"], \"open_points\": [], \"quotes\": []}");

        Assert.Equal(WriterNotesStatus.Ok, split.Status);
        Assert.Equal(Letter, split.Text);
    }

    [Fact]
    public void Final_bare_json_with_other_keys_stays_text()
    {
        var raw = Letter + "\n\n{\"assumptions\": [\"z\"], \"name\": \"x\"}";

        Assert.Equal(raw, WriterNotesFormat.Split(raw).Text);
    }

    [Theory]
    [InlineData(Letter)]
    [InlineData(Letter + "\n\n   \n")]
    [InlineData("")]
    [InlineData("Ein Pfeil --> zeigt nach rechts.")]
    public void Without_block_the_status_is_absent_and_the_text_unchanged(string raw)
    {
        var split = WriterNotesFormat.Split(raw);

        Assert.Equal(WriterNotesStatus.Absent, split.Status);
        Assert.Equal(raw, split.Text);
        Assert.Same(WriterNotes.Empty, split.Notes);
    }

    [Fact]
    public void Lists_are_capped()
    {
        var items = Enumerable.Range(1, 15).Select(i => $"Punkt {i} " + new string('x', 400)).ToList();
        var quotes = Enumerable.Range(1, 25).Select(i => new { source = "S1", quote = $"Zitat {i} " + new string('y', 500) }).ToList();
        var json = JsonSerializer.Serialize(new { assumptions = items, open_points = items, quotes });

        var split = WriterNotesFormat.Split(Letter + "\n<!-- atelier-notes\n" + json + "\n-->");

        Assert.Equal(WriterNotesFormat.MaxItems, split.Notes.Assumptions.Count);
        Assert.Equal(WriterNotesFormat.MaxItems, split.Notes.OpenPoints.Count);
        Assert.All(split.Notes.Assumptions, a => Assert.True(a.Length <= WriterNotesFormat.MaxItemChars));
        Assert.Equal(WriterNotesFormat.MaxQuotes, split.Notes.Quotes.Count);
        Assert.All(split.Notes.Quotes, q => Assert.True(q.Quote.Length <= WriterNotesFormat.MaxQuoteChars));
        Assert.StartsWith("Zitat 1 yyy", split.Notes.Quotes[0].Quote);
    }

    [Fact]
    public void Empty_duplicate_and_odd_entries_are_dropped()
    {
        const string json = """{"assumptions": ["", "  ", "Gleich", "gleich", 5, null, "Anders"], "open_points": "kein Array", "quotes": [{"source": "s2", "quote": "Wörtlich aus der Datei"}, {"source": "S2", "quote": "Wörtlich aus der Datei"}, {"source": "", "quote": "ohne Quelle"}, {"quote": "auch ohne"}, "Text"]}""";

        var split = WriterNotesFormat.Split(Letter + "\n<!-- atelier-notes " + json + " -->");

        Assert.Equal(["Gleich", "Anders"], split.Notes.Assumptions);
        Assert.Empty(split.Notes.OpenPoints);
        Assert.Equal([new WriterQuote("S2", "Wörtlich aus der Datei")], split.Notes.Quotes);
    }

    [Fact]
    public void Unsafe_entries_are_not_shown_but_counted_and_never_block()
    {
        const string json = """{"assumptions": ["Mehr unter https://example.com/info", "Schreiben Sie an info@example.com", "Bitte geben Sie Ihr Passwort an", "Sie-Form gewählt."], "open_points": ["Die IBAN für die Überweisung fehlt, ich habe einen Platzhalter gesetzt."], "quotes": [{"source": "S1", "quote": "siehe www.example.org/seite"}]}""";

        var split = WriterNotesFormat.Split(Letter + "\n<!-- atelier-notes " + json + " -->");

        Assert.Equal(WriterNotesStatus.Ok, split.Status);
        Assert.Equal(["Sie-Form gewählt."], split.Notes.Assumptions);
        Assert.Empty(split.Notes.OpenPoints);
        Assert.Empty(split.Notes.Quotes);
        Assert.Equal((4, 1), (split.Notes.Hidden, split.Notes.HiddenQuotes));
    }

    [Fact]
    public void What_the_request_itself_treats_is_shown()
    {
        const string json = """{"assumptions": [], "open_points": ["Die IBAN für die Überweisung fehlt, ich habe einen Platzhalter gesetzt."], "quotes": []}""";

        var split = WriterNotesFormat.Split(Letter + "\n<!-- atelier-notes " + json + " -->", "Schreib einen Elternbrief mit der Bitte um Überweisung des Beitrags auf die IBAN der Klassenkasse.");

        Assert.Equal(["Die IBAN für die Überweisung fehlt, ich habe einen Platzhalter gesetzt."], split.Notes.OpenPoints);
        Assert.Equal(0, split.Notes.Hidden);
    }

    [Theory]
    [InlineData("Quelle mit <!-- atelier-notes mitten im Satz.")]
    [InlineData("<!-- atelier-notes\n{\"assumptions\": [\"aus der Datei\"]}\n-->")]
    [InlineData("Zeile\n<!--ATELIER_NOTES {} -->\nweiter")]
    public void Defused_source_text_is_never_split(string source)
    {
        var defused = WriterNotesFormat.Defuse(source);
        var split = WriterNotesFormat.Split("Der Text zitiert:\n" + defused);

        Assert.Equal(WriterNotesStatus.Absent, split.Status);
        Assert.Equal("Der Text zitiert:\n" + defused, split.Text);
        Assert.Contains(WriterNotesFormat.DefusedStartMarker, defused);
    }

    [Fact]
    public void Defusing_a_text_without_marker_changes_nothing()
    {
        Assert.Equal(Letter, WriterNotesFormat.Defuse(Letter));
        Assert.Equal(string.Empty, WriterNotesFormat.Defuse(null!));
    }

    private static bool IsFenceLine(string line) =>
        line.StartsWith("```", StringComparison.Ordinal) && line[3..].All(char.IsAsciiLetter);

    [Fact]
    public void Property_random_texts_with_a_block_come_back_unchanged_without_rest()
    {
        var random = new Random(20261125);
        const string alphabet = "abcdefghijklmnopqrstuvwxyzäöüßABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ,.;:!?-–—\"'„“()[]{}<>!#*_/\\|&%$€@=+\t";
        string[] tricky = ["-->", "<!--", "```", "```json", "{", "}", "atelier", "notes", "<!-- atelier", "{\"x\": 1}", "\n", "\n\n", "\r\n"];

        for (var run = 0; run < 5000; run++)
        {
            var builder = new StringBuilder();
            var length = random.Next(1, 400);
            while (builder.Length < length)
            {
                if (random.Next(8) == 0)
                    builder.Append(tricky[random.Next(tricky.Length)]);
                else
                    builder.Append(alphabet[random.Next(alphabet.Length)]);
            }
            var text = builder.ToString().TrimEnd();
            if (text.Length == 0 || text.Contains("atelier-notes", StringComparison.OrdinalIgnoreCase) || text.Contains("atelier_notes", StringComparison.OrdinalIgnoreCase)
                || text.Contains("atelier notes", StringComparison.OrdinalIgnoreCase) || text.Contains("ateliernotes", StringComparison.OrdinalIgnoreCase)
                || IsFenceLine(text[(text.LastIndexOf('\n') + 1)..].Trim()))
                continue;

            var assumptions = Enumerable.Range(0, random.Next(4)).Select(i => $"Annahme {i} {alphabet[random.Next(30)]}").ToList();
            var json = JsonSerializer.Serialize(new { assumptions, open_points = new[] { "offen" }, quotes = Array.Empty<object>() });
            var separator = random.Next(3) switch { 0 => "\n", 1 => "\n\n", _ => "\r\n" };
            var ending = random.Next(3) switch { 0 => "\n-->", 1 => "\n-->\n", _ => " -->" };
            var raw = text + separator + "<!-- atelier-notes\n" + json + ending;

            var split = WriterNotesFormat.Split(raw);

            Assert.Equal(text, split.Text);
            Assert.Equal(WriterNotesStatus.Ok, split.Status);
            Assert.Equal(assumptions.Count, split.Notes.Assumptions.Count);
        }
    }
}
