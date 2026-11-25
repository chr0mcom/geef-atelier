using System.Text.Json.Nodes;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Infrastructure.Research;

namespace Geef.Atelier.Tests.Research;

/// <summary>
/// <see cref="DossierOutputParser"/>: keeps the model's passages unchanged, drops and counts unknown pages and topics and overlong quotes
/// (never shortened), caps at 40 and returns <c>null</c> only for unusable JSON.
/// </summary>
public sealed class DossierOutputParserTests
{
    private static readonly string[] PageIds = ["P1", "P2"];

    private static string Answer(params JsonObject[] passages) =>
        new JsonObject { ["passages"] = new JsonArray(passages.Select(p => (JsonNode)p).ToArray()) }.ToJsonString();

    private static JsonObject Passage(JsonNode? topic, JsonNode? page, JsonNode? quote)
    {
        var item = new JsonObject();
        if (topic is not null)
            item["topic"] = topic;
        if (page is not null)
            item["page"] = page;
        if (quote is not null)
            item["quote"] = quote;
        return item;
    }

    [Fact]
    public void Valid_passages_are_kept_word_for_word()
    {
        var quote = "  Christmas Day is a bank holiday in England.\n";
        var result = DossierOutputParser.Parse(Answer(Passage(2, "P2", quote), Passage(1, "p1", "Der Satz bleibt so.")), PageIds, 2);

        Assert.NotNull(result);
        Assert.Equal([new ProposedPassage(2, "P2", quote), new ProposedPassage(1, "P1", "Der Satz bleibt so.")], result.Passages);
        Assert.Empty(result.Dropped);
    }

    [Fact]
    public void An_overlong_quote_is_dropped_never_shortened()
    {
        var limit = new string('a', DossierBuilder.MaxQuoteChars);
        var result = DossierOutputParser.Parse(Answer(Passage(1, "P1", limit), Passage(1, "P1", limit + "b")), PageIds, 2)!;

        Assert.Equal(limit, Assert.Single(result.Passages).Quote);
        Assert.Equal(1, result.Dropped[ResearchDropReason.OverlongQuote]);
    }

    [Fact]
    public void Unknown_pages_and_topics_and_malformed_items_are_counted_as_unknown_page()
    {
        var json = new JsonObject
        {
            ["passages"] = new JsonArray(
                Passage(1, "P3", "An unknown page."),
                Passage(3, "P1", "An unknown topic."),
                Passage(0, "P1", "Topic zero."),
                Passage(1.5, "P1", "A broken topic."),
                Passage("1", "P1", "A topic as text."),
                Passage(1, 7, "A page as number."),
                Passage(null, "P1", "No topic."),
                Passage(1, null, "No page."),
                "just text",
                Passage(1, "P1", "Kept.")),
        }.ToJsonString();

        var result = DossierOutputParser.Parse(json, PageIds, 2)!;

        Assert.Equal("Kept.", Assert.Single(result.Passages).Quote);
        Assert.Equal(new Dictionary<ResearchDropReason, int> { [ResearchDropReason.UnknownPage] = 9 }, result.Dropped);
    }

    [Fact]
    public void A_missing_quote_is_passed_on_empty_for_the_builder_to_count()
    {
        var result = DossierOutputParser.Parse(Answer(Passage(1, "P1", null), Passage(1, "P1", 5)), PageIds, 2)!;

        Assert.Equal(["", ""], result.Passages.Select(p => p.Quote));
    }

    [Fact]
    public void More_than_forty_passages_are_capped_and_counted()
    {
        var passages = Enumerable.Range(1, 45).Select(i => Passage(1, "P1", $"Passage number {i} of the page.")).Append(Passage(1, "P9", "Unknown.")).ToArray();

        var result = DossierOutputParser.Parse(Answer(passages), PageIds, 1)!;

        Assert.Equal(DossierOutputParser.MaxPassages, result.Passages.Count);
        Assert.Equal("Passage number 40 of the page.", result.Passages[^1].Quote);
        Assert.Equal(5, result.Dropped[ResearchDropReason.OverLimit]);
        Assert.Equal(1, result.Dropped[ResearchDropReason.UnknownPage]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"passage":[]}""")]
    [InlineData("""{"passages":"none"}""")]
    [InlineData("""{"passages":[""")]
    public void Unusable_json_is_null(string json) => Assert.Null(DossierOutputParser.Parse(json, PageIds, 2));

    [Fact]
    public void An_empty_list_is_a_normal_answer()
    {
        var result = DossierOutputParser.Parse("""{"passages":[]}""", PageIds, 2)!;

        Assert.Empty(result.Passages);
        Assert.Empty(result.Dropped);
    }
}
