using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Domain.Research;

public sealed class ResearchJsonTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 28, 9, 30, 15, TimeSpan.Zero);

    private static OrderResearch Sample() => new(OrderResearch.CurrentSchemaVersion, ResearchStatus.Found, ["Mindestlohn 2026", "Feiertage England 2027"], 1, 2,
        2, 1, 5, 7, new Dictionary<ResearchDropReason, int> { [ResearchDropReason.NotInPage] = 3, [ResearchDropReason.OverlongQuote] = 1 },
        [
            new ResearchEntry("W1.1", 1, "Der gesetzliche Mindestlohn steigt auf 13,90 Euro „brutto“.", "https://amt.example/lohn", "Mindestlohn – Amt", At,
                QuoteMatchKind.Normalized, new string('a', 64), 120, 58),
        ],
        "claude-sonnet", 18_250, null, At);

    [Fact]
    public void Round_trip_keeps_every_field()
    {
        var original = Sample();

        var parsed = ResearchJson.TryParse(ResearchJson.Serialize(original));

        Assert.NotNull(parsed);
        Assert.Equal(ResearchJson.Serialize(original), ResearchJson.Serialize(parsed));
        Assert.Equal(original.TopicsSent, parsed.TopicsSent);
        Assert.Equal(original.Dropped, parsed.Dropped);
        Assert.Equal(original.Entries, parsed.Entries);
        Assert.Equal(original with { TopicsSent = parsed.TopicsSent, Dropped = parsed.Dropped, Entries = parsed.Entries }, parsed);
    }

    [Fact]
    public void Json_is_camel_case_with_enums_as_text()
    {
        var json = ResearchJson.Serialize(Sample());

        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"status\":\"Found\"", json);
        Assert.Contains("\"NotInPage\":3", json);
        Assert.Contains("\"match\":\"Normalized\"", json);
        Assert.DoesNotContain("\"errorCode\"", json);
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        var json = ResearchJson.Serialize(Sample()).Replace("{\"schemaVersion\":1,", "{\"schemaVersion\":1,\"later\":{\"x\":[1,2]},", StringComparison.Ordinal)
            .Replace("\"quote\":", "\"extra\":true,\"quote\":", StringComparison.Ordinal);

        var parsed = ResearchJson.TryParse(json);

        Assert.NotNull(parsed);
        Assert.Equal("W1.1", Assert.Single(parsed.Entries).Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"status\":\"SomethingNew\"}")]
    public void Null_blank_or_unreadable_json_gives_null(string? json) =>
        Assert.Null(ResearchJson.TryParse(json));

    [Fact]
    public void Missing_lists_become_empty_and_entries_without_a_passage_are_left_out()
    {
        var parsed = ResearchJson.TryParse("""{"schemaVersion":1,"status":"NothingFound","entries":[null,{"id":"W1.1","quote":"  "}],"at":"2026-11-28T09:30:15+00:00"}""");

        Assert.NotNull(parsed);
        Assert.Equal(ResearchStatus.NothingFound, parsed.Status);
        Assert.Empty(parsed.TopicsSent);
        Assert.Empty(parsed.Dropped);
        Assert.Empty(parsed.Entries);
    }
}
