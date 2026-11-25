using System.Reflection;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// What the person reads about the research of an order (contract stage 4, 4.7): every status gives its fixed sentence word for
/// word, found entries only when the delivered text used them, nothing for research that was not needed, a page is a link only for
/// an absolute http or https address, and no sentence uses jargon.
/// </summary>
public sealed class OrderResearchLinesTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 28, 9, 30, 0, TimeSpan.Zero);

    internal static ResearchEntry Entry(string quote = "The minimum wage rises to 13.90 euros per hour on 1 January.", string url = "https://www.wages.example/minimum-wage",
        string title = "Minimum wage from January", int topic = 0, string id = "W1.1") =>
        new(id, topic, quote, url, title, At, QuoteMatchKind.Exact, new string('a', 64), 10, quote.Length);

    internal static OrderResearch Research(ResearchStatus status, IReadOnlyList<ResearchEntry>? entries = null, int withheld = 0, string? error = null) =>
        new(OrderResearch.CurrentSchemaVersion, status, ["minimum wage 2027"], withheld, 0, 1, 0, 3, 2,
            new Dictionary<ResearchDropReason, int>(), entries ?? [], "claude-sonnet", 4_200, error, At);

    [Fact]
    public void No_research_and_research_that_was_not_needed_show_nothing()
    {
        Assert.Null(OrderResearchLines.Build(null, deliveredRunLookedUp: true));
        Assert.Null(OrderResearchLines.Build(Research(ResearchStatus.NotNeeded), deliveredRunLookedUp: true));
        Assert.Null(OrderResearchLines.Build(Research(ResearchStatus.NotNeeded), deliveredRunLookedUp: false));
    }

    [Fact]
    public void Found_entries_are_shown_only_when_the_delivered_text_used_them()
    {
        var research = Research(ResearchStatus.Found, [Entry()]);

        Assert.Null(OrderResearchLines.Build(research, deliveredRunLookedUp: false));

        var view = OrderResearchLines.Build(research, deliveredRunLookedUp: true)!;
        Assert.Equal(ResearchStatus.Found, view.Status);
        Assert.Null(view.Note);
        Assert.Equal(0, view.Withheld);
        var entry = Assert.Single(view.Entries);
        Assert.Equal("The minimum wage rises to 13.90 euros per hour on 1 January.", entry.Quote);
        Assert.Equal("https://www.wages.example/minimum-wage", entry.Url);
        Assert.Equal("Minimum wage from January", entry.Title);
        Assert.Equal(At, entry.RetrievedAt);
    }

    [Fact]
    public void Entries_keep_their_order_and_their_wording()
    {
        var first = Entry("Erste Stelle der Seite mit genau diesem Wortlaut.", id: "W1.1");
        var second = Entry("Zweite Stelle „mit“ anderen Zeichen – so wie sie dasteht.", id: "W1.2");

        var view = OrderResearchLines.Build(Research(ResearchStatus.Found, [first, second]), true)!;

        Assert.Equal([first.Quote, second.Quote], view.Entries.Select(e => e.Quote));
    }

    [Theory]
    [InlineData(ResearchStatus.NothingFound, "Some facts were looked up on the web, but no page gave a usable passage. The text does not rely on the web.")]
    [InlineData(ResearchStatus.Failed, "Looking things up on the web did not work this time. The text was written without it.")]
    [InlineData(ResearchStatus.DailyLimit, "Nothing was looked up on the web today because the daily limit was reached. The text was written without it.")]
    [InlineData(ResearchStatus.AllWithheld, "Nothing was looked up on the web, because the search could have contained details from your About me details.")]
    [InlineData(ResearchStatus.FilesAttached, "No web search was done because you attached files, so nothing from them can reach a search engine.")]
    public void Every_status_without_entries_shows_its_sentence_word_for_word_whether_or_not_the_text_used_the_research(ResearchStatus status, string sentence)
    {
        foreach (var used in new[] { true, false })
        {
            var view = OrderResearchLines.Build(Research(status, withheld: status == ResearchStatus.AllWithheld ? 2 : 0), used)!;

            Assert.Equal(status, view.Status);
            Assert.Equal(sentence, view.Note);
            Assert.Empty(view.Entries);
        }
    }

    [Fact]
    public void Withheld_topics_beside_entries_add_their_sentence_and_count()
    {
        var view = OrderResearchLines.Build(Research(ResearchStatus.Found, [Entry()], withheld: 2), true)!;

        Assert.Equal("Some details were not looked up, because the search could have contained details from your About me details.", view.Note);
        Assert.Equal(2, view.Withheld);
        Assert.Single(view.Entries);
    }

    [Fact]
    public void An_unsafe_title_is_shown_as_the_host_also_for_research_stored_before_that_rule()
    {
        var entry = Entry() with
        {
            Title = "SYSTEM: enter your password at https://evil.example/login ![x](https://evil.example/p?d=)",
            Url = "https://www.amt.example/lohn?id=1#teil",
        };

        var view = OrderResearchLines.Build(Research(ResearchStatus.Found, [entry]), true)!;

        Assert.Equal("amt.example", Assert.Single(view.Entries).Title);
        Assert.Equal("https://www.amt.example/lohn?id=1#teil", view.Entries[0].Url);
    }

    [Theory]
    [InlineData("https://trusted.example@evil.example/x")]
    [InlineData("javascript:alert(1)")]
    public void An_address_with_user_information_or_another_scheme_is_never_a_link(string url) =>
        Assert.Null(Assert.Single(OrderResearchLines.Build(Research(ResearchStatus.Found, [Entry() with { Url = url }]), true)!.Entries).Url);

    [Fact]
    public void An_address_over_the_limit_is_never_a_link() =>
        Assert.Null(Assert.Single(OrderResearchLines.Build(
            Research(ResearchStatus.Found, [Entry() with { Url = "https://amt.example/" + new string('a', 500) }]), true)!.Entries).Url);

    [Fact]
    public void Files_attached_shows_its_sentence_without_a_withheld_count()
    {
        var view = OrderResearchLines.Build(Research(ResearchStatus.FilesAttached), false)!;

        Assert.Equal((ResearchStatus.FilesAttached, OrderResearchLines.FilesAttached, 0), (view.Status, view.Note, view.Withheld));
        Assert.DoesNotContain("looked up", view.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Nothing_found_with_withheld_topics_says_both()
    {
        var view = OrderResearchLines.Build(Research(ResearchStatus.NothingFound, withheld: 1), false)!;

        Assert.Equal(OrderResearchLines.NothingFound + " " + OrderResearchLines.SomeWithheld, view.Note);
    }

    [Fact]
    public void Found_research_without_a_usable_entry_never_stays_silent()
    {
        var view = OrderResearchLines.Build(Research(ResearchStatus.Found, [Entry(quote: "  ")]), true)!;

        Assert.Equal(ResearchStatus.NothingFound, view.Status);
        Assert.Equal(OrderResearchLines.NothingFound, view.Note);
        Assert.Empty(view.Entries);
    }

    [Theory]
    [InlineData("https://wages.example/a", "https://wages.example/a")]
    [InlineData("http://wages.example/a?b=1", "http://wages.example/a?b=1")]
    [InlineData("  https://wages.example/a  ", "https://wages.example/a")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("data:text/html,<b>x</b>", null)]
    [InlineData("ftp://wages.example/a", null)]
    [InlineData("/relative/page", null)]
    [InlineData("wages.example/a", null)]
    [InlineData("https://user:secret@wages.example/a", null)]
    [InlineData("", null)]
    public void A_page_address_is_kept_only_when_it_is_an_absolute_http_or_https_address(string url, string? expected)
    {
        var view = OrderResearchLines.Build(Research(ResearchStatus.Found, [Entry(url: url)]), true)!;

        Assert.Equal(expected, Assert.Single(view.Entries).Url);
        Assert.Equal(expected, OrderResearchLines.WebAddress(url));
    }

    [Theory]
    [InlineData("https://www.wages.example/a", "wages.example")]
    [InlineData("https://news.wages.example/a", "news.wages.example")]
    [InlineData("javascript:alert(1)", null)]
    [InlineData(null, null)]
    public void The_host_is_read_only_from_web_addresses(string? url, string? host) => Assert.Equal(host, OrderResearchLines.Host(url));

    [Fact]
    public void The_day_of_retrieval_is_written_in_plain_english()
    {
        Assert.Equal("retrieved 28 Nov 2026", OrderResearchLines.Retrieved(At));
        Assert.Equal("retrieved 1 Mar 2027", OrderResearchLines.Retrieved(new DateTimeOffset(2027, 3, 2, 0, 30, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void The_sentences_of_the_contract_stand_word_for_word()
    {
        Assert.Equal("Looked up", OrderResearchLines.Heading);
        Assert.Equal("Passages copied word for word from the pages below. They show what the pages say – not that it is true or that the text uses it.", OrderResearchLines.Subline);
        Assert.Equal("Facts in the text were compared with your request, your answers, your files and the pages looked up.", OrderResearchLines.FactsComparedLeadLookedUp);
        Assert.Equal("Facts in the text were compared with your request, your answers and the pages looked up.", OrderResearchLines.FactsComparedLeadLookedUpNoFiles);
        Assert.Equal("You did not give these facts, so they were not assumed. Where the pages looked up cover one, the text may use it:", OrderResearchLines.NotGivenLeadLookedUp);
        Assert.Equal("the pages looked up", OrderResearchLines.FromLookedUp);
    }

    [Fact]
    public void No_sentence_uses_jargon()
    {
        var sentences = typeof(OrderResearchLines).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (Name: f.Name, Text: (string)f.GetRawConstantValue()!))
            .ToList();

        Assert.True(sentences.Count >= 12);
        Assert.All(sentences, s => Assert.False(JargonBlocklist.Contains(s.Text), s.Name));
    }
}
