using System.Net;
using System.Text.Json;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Research;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Research;

/// <summary>
/// <see cref="OrderResearchService"/>: every condition of contract 4.1 on its own, the stored research instead of a new one, the daily
/// limit, withheld topics without a search, partial success, every failure as a status without an exception, the lost conditional
/// write, one log line without contents, the audit only with directory and override, and the privacy of what leaves the system.
/// </summary>
public sealed class OrderResearchServiceTests
{
    private const string User = "writer-a";
    private const string Request = "Schreib einen Aushang zum neuen Mindestlohn für unsere Mitarbeitenden.";
    private const string TopicA = "Mindestlohn 2026 Höhe";
    private const string TopicB = "Mindestlohn Stichtag Erhöhung";
    private const string PageA = "Ab dem 1. Januar 2026 beträgt der gesetzliche Mindestlohn 13,90 Euro brutto je Stunde. Weitere Angaben folgen.";
    private const string PageB = "Die Erhöhung gilt ab dem ersten Januar für alle Beschäftigten in Deutschland. Ausnahmen sind selten.";
    private const string QuoteA = "Ab dem 1. Januar 2026 beträgt der gesetzliche Mindestlohn 13,90 Euro brutto je Stunde.";
    private const string QuoteB = "Die Erhöhung gilt ab dem ersten Januar für alle Beschäftigten in Deutschland.";

    private sealed record Setup(OrderResearchService Service, ResearchOrders Orders, FakeSearch Search, FakeCondenser Condenser, RecordingLogger<OrderResearchService> Log);

    private static ResearchOptions On(Action<ResearchOptions>? change = null)
    {
        var options = new ResearchOptions { Mode = ResearchMode.On };
        change?.Invoke(options);
        return options;
    }

    private static Setup Create(ResearchOptions? options = null, FakeSearch? search = null, FakeCondenser? condenser = null, TimeProvider? time = null)
    {
        var orders = new ResearchOrders();
        search ??= new FakeSearch(Hits);
        condenser ??= new FakeCondenser((_, pages) => Proposals(pages));
        var log = new RecordingLogger<OrderResearchService>();
        var service = new OrderResearchService(orders, search, condenser, Options.Create(options ?? On()), log, time ?? TimeProvider.System);
        return new Setup(service, orders, search, condenser, log);
    }

    private static TavilySearchOutcome Hits(string topic) => topic switch
    {
        TopicA => new([new TavilyHit("https://example.org/lohn", "Mindestlohn aktuell", PageA)], null, 5),
        TopicB => new([new TavilyHit("https://example.org/stichtag", "Stichtag", PageB), new TavilyHit("https://example.org/lohn", "Mindestlohn aktuell", PageA)], null, 5),
        _ => new([], null, 5),
    };

    private static CondenseOutcome Proposals(IReadOnlyList<ResearchPage> pages)
    {
        var a = pages.Single(p => p.Url.EndsWith("/lohn", StringComparison.Ordinal)).Id;
        var proposals = new List<ProposedPassage> { new(1, a, QuoteA), new(1, a, "Der Mindestlohn steigt auf 15 Euro pro Stunde.") };
        if (pages.FirstOrDefault(p => p.Url.EndsWith("/stichtag", StringComparison.Ordinal)) is { } b)
            proposals.Add(new(2, b.Id, "Die  Erhöhung gilt ab dem ersten Januar für alle Beschäftigten in Deutschland."));
        return new CondenseOutcome(proposals, new Dictionary<ResearchDropReason, int> { [ResearchDropReason.UnknownPage] = 2 }, "claude-sonnet-latest", null, 7);
    }

    private static string Clarify(IntakeRecordStatus status, params string[] topics) =>
        IntakeRecordJson.Serialize(IntakeRecord.Fallback(status, null, 1, "m", BriefMode.AppendAnswers) with { ResearchTopics = topics });

    private static OrderEntity Order(string? clarify, string? user = User, string? researchJson = null) => new()
    {
        Id = Guid.NewGuid(), CreatedByUser = user, RequestText = Request, Mode = OrderMode.Direct, State = OrderState.Ready, Entry = OrderEntry.Web,
        ClarifyJson = clarify, ResearchJson = researchJson, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Task<OrderResearch?> Ensure(Setup s, OrderEntity order, int? version = 1, bool revises = false, bool files = false, string? profile = null) =>
        s.Service.EnsureAsync(s.Orders.Put(order), version, revises, files, profile, CancellationToken.None);

    private static void AssertNothingHappened(Setup s, OrderResearch? result)
    {
        Assert.Null(result);
        Assert.Empty(s.Search.Queries);
        Assert.Empty(s.Condenser.Calls);
        Assert.Equal((0, 0, 0), (s.Orders.Gets, s.Orders.Sets, s.Orders.Counts));
    }

    [Fact]
    public async Task A_researchable_gap_is_looked_up_and_stored_with_the_wording_of_the_page()
    {
        var s = Create();
        var order = Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB));

        var result = await Ensure(s, order);

        Assert.NotNull(result);
        Assert.Equal(ResearchStatus.Found, result.Status);
        Assert.Equal([TopicA, TopicB], s.Search.Queries.Order(StringComparer.Ordinal));
        Assert.Equal([TopicA, TopicB], result.TopicsSent);
        Assert.Equal((2, 0, 2), (result.Searches, result.SearchesFailed, result.Pages));
        Assert.Equal([QuoteA, QuoteB], result.Entries.Select(e => e.Quote));
        Assert.Equal(["W1.1", "W1.2"], result.Entries.Select(e => e.Id));
        Assert.Equal(5, result.Proposed);
        Assert.Equal(new Dictionary<ResearchDropReason, int> { [ResearchDropReason.UnknownPage] = 2, [ResearchDropReason.NotInPage] = 1 }, result.Dropped);
        Assert.Equal("claude-sonnet-latest", result.Model);
        Assert.Null(result.ErrorCode);

        var pages = Assert.Single(s.Condenser.Calls).Pages;
        Assert.Equal(["P1", "P2"], pages.Select(p => p.Id));
        Assert.Equal(["https://example.org/lohn", "https://example.org/stichtag"], pages.Select(p => p.Url));
        Assert.Equal([1, 2], pages.Select(p => p.Topic));

        var stored = s.Orders.Find(order.Id)!;
        Assert.Equal(ResearchJson.Serialize(result), stored.ResearchJson);
        Assert.NotNull(stored.ResearchAt);
        Assert.True(stored.ResearchAt <= result.At);
    }

    [Fact]
    public async Task Mode_off_returns_nothing_without_reading_or_writing()
    {
        var s = Create(new ResearchOptions { Mode = ResearchMode.Off });

        AssertNothingHappened(s, await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA))));
        AssertNothingHappened(s, await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA), researchJson: """{"schemaVersion":1,"status":"Found"}""")));
    }

    [Fact]
    public async Task An_override_switches_research_on_or_off_for_one_user()
    {
        var on = Create(new ResearchOptions { Mode = ResearchMode.Off, ModeOverrides = new(StringComparer.OrdinalIgnoreCase) { [User] = ResearchMode.On } });
        Assert.Equal(ResearchStatus.Found, (await Ensure(on, Order(Clarify(IntakeRecordStatus.Ok, TopicA))))!.Status);
        var other = Create(new ResearchOptions { Mode = ResearchMode.Off, ModeOverrides = new(StringComparer.OrdinalIgnoreCase) { [User] = ResearchMode.On } });
        AssertNothingHappened(other, await Ensure(other, Order(Clarify(IntakeRecordStatus.Ok, TopicA), "someone-else")));
        var anonymous = Create(new ResearchOptions { Mode = ResearchMode.Off, ModeOverrides = new(StringComparer.OrdinalIgnoreCase) { [User] = ResearchMode.On } });
        AssertNothingHappened(anonymous, await Ensure(anonymous, Order(Clarify(IntakeRecordStatus.Ok, TopicA), null)));

        var off = Create(On(o => o.ModeOverrides[User] = ResearchMode.Off));
        AssertNothingHappened(off, await Ensure(off, Order(Clarify(IntakeRecordStatus.Ok, TopicA))));
    }

    [Fact]
    public void Overrides_from_configuration_match_the_user_name_without_regard_to_case()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Research:Mode"] = "Off",
            ["Research:ModeOverrides:Writer-A"] = "On",
        }).Build();
        var services = new ServiceCollection();
        services.AddResearch(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<ResearchOptions>>().Value;

        Assert.Equal(ResearchMode.On, options.ModeOverrides["writer-a"]);
        Assert.Equal(ResearchMode.Off, options.Mode);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task Only_the_first_run_without_a_text_to_revise_researches(int? version, bool revises)
    {
        var s = Create();
        var order = Order(Clarify(IntakeRecordStatus.Ok, TopicA));
        AssertNothingHappened(s, await Ensure(s, order, version, revises));
        Assert.Null(s.Orders.Find(order.Id)!.ResearchJson);
    }

    [Fact]
    public async Task Without_a_usable_clarification_or_topics_nothing_is_researched()
    {
        var s = Create();
        AssertNothingHappened(s, await Ensure(s, Order(null)));
        AssertNothingHappened(s, await Ensure(s, Order("not json")));
        AssertNothingHappened(s, await Ensure(s, Order(Clarify(IntakeRecordStatus.Failed, TopicA))));
        AssertNothingHappened(s, await Ensure(s, Order(Clarify(IntakeRecordStatus.TimedOut, TopicA))));
        AssertNothingHappened(s, await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok))));
        AssertNothingHappened(s, await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, " ", ""))));
    }

    [Fact]
    public async Task Stored_research_is_used_unchanged_by_every_later_run()
    {
        var first = Create();
        var order = Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB));
        var research = await Ensure(first, order);
        var stored = first.Orders.Find(order.Id)!;

        foreach (var (version, revises) in new (int?, bool)[] { (1, false), (2, false), (3, true), (null, false) })
        {
            var later = Create();
            var again = await later.Service.EnsureAsync(later.Orders.Put(stored), version, revises, false, null, CancellationToken.None);

            Assert.Equal(ResearchJson.Serialize(research!), ResearchJson.Serialize(again!));
            Assert.Empty(later.Search.Queries);
            Assert.Empty(later.Condenser.Calls);
            Assert.Equal((0, 0), (later.Orders.Sets, later.Orders.Counts));
        }
    }

    [Fact]
    public async Task Unreadable_stored_research_gives_nothing_and_is_never_replaced()
    {
        var s = Create();
        var order = Order(Clarify(IntakeRecordStatus.Ok, TopicA), researchJson: "{broken");

        Assert.Null(await Ensure(s, order));
        Assert.Empty(s.Search.Queries);
        Assert.Equal("{broken", s.Orders.Find(order.Id)!.ResearchJson);
        Assert.Contains(s.Log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task The_daily_limit_stops_the_search_and_is_stored_as_a_status()
    {
        var now = new DateTimeOffset(2026, 11, 28, 23, 59, 0, TimeSpan.Zero);
        var s = Create(On(o => o.DailyOrderCap = 3), time: new FixedClock(now));
        s.Orders.Put(Order(null) with { ResearchJson = "{}", ResearchAt = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddTicks(-1) });
        s.Orders.ExtraCount = 2;

        var below = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));
        Assert.Equal(ResearchStatus.Found, below!.Status);

        var order = Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB));
        var limited = await Ensure(s, order);

        Assert.Equal(ResearchStatus.DailyLimit, limited!.Status);
        Assert.Empty(limited.TopicsSent);
        Assert.Equal(0, limited.Searches);
        Assert.Single(s.Search.Queries);
        Assert.Single(s.Condenser.Calls);
        Assert.Equal(ResearchJson.Serialize(limited), s.Orders.Find(order.Id)!.ResearchJson);
    }

    [Fact]
    public async Task Topics_with_words_from_the_profile_are_withheld_and_never_searched()
    {
        var s = Create();
        var order = Order(Clarify(IntakeRecordStatus.Ok, "Firma Kranichsteiner Mindestlohn", "Tarif Oberhuber Mindestlohn"));

        var result = await Ensure(s, order, profile: "Organization: Kranichsteiner GmbH\nAnything else: Ich heiße Oberhuber.");

        Assert.Equal(ResearchStatus.AllWithheld, result!.Status);
        Assert.Equal(2, result.Withheld);
        Assert.Empty(result.TopicsSent);
        Assert.Empty(s.Search.Queries);
        Assert.Empty(s.Condenser.Calls);
        Assert.Equal(0, s.Orders.Counts);
        Assert.NotNull(s.Orders.Find(order.Id)!.ResearchJson);
    }

    [Fact]
    public async Task The_field_names_of_the_profile_never_withhold_a_topic()
    {
        var s = Create();
        string[] topics = ["notice period when moving out of a flat in England", "bank holidays in the UK 2026", "consumer rights online order refund deadline", "Signaturgesetz Pflicht"];
        var profile = ProfilePrompt.Render(new UserProfileEntity
        {
            Username = User, Formality = Formality.Formal, Language = "de", Signature = "Viele Grüße", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, topics)), profile: profile);

        Assert.Equal(0, result!.Withheld);
        Assert.Equal(topics.Order(StringComparer.Ordinal), s.Search.Queries.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task With_free_text_in_the_profile_a_topic_in_another_language_than_the_order_is_withheld()
    {
        var s = Create();
        var clarify = IntakeRecordJson.Serialize(IntakeRecord.Fallback(IntakeRecordStatus.Ok, null, 1, "m", BriefMode.AppendAnswers) with
        {
            Output = new ClarifierOutput("Aushang", "de", "Ein Aushang.", false, null, LengthScale.Standard, [], [TopicA, "sick leave rules for the employer"]),
            ResearchTopics = [TopicA, "sick leave rules for the employer"],
        });

        var withFreeText = await Ensure(s, Order(clarify), profile: "Anything else: Ich bin seit Mai krankgeschrieben.");
        var withoutFreeText = await Ensure(Create(), Order(clarify), profile: "Role: Teamleitung");

        Assert.Equal([TopicA], withFreeText!.TopicsSent);
        Assert.Equal(1, withFreeText.Withheld);
        Assert.Equal(0, withoutFreeText!.Withheld);
    }

    [Fact]
    public async Task An_order_with_files_never_searches_and_stores_files_attached_for_every_run()
    {
        var s = Create();
        var order = Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB));

        var result = await Ensure(s, order, files: true);

        Assert.Equal((ResearchStatus.FilesAttached, 0, 0, 0), (result!.Status, result.Searches, result.Withheld, result.Entries.Count));
        Assert.Empty(result.TopicsSent);
        Assert.Empty(s.Search.Queries);
        Assert.Empty(s.Condenser.Calls);
        Assert.Equal(0, s.Orders.Counts);
        var stored = s.Orders.Find(order.Id)!;
        Assert.Equal(ResearchJson.Serialize(result), stored.ResearchJson);
        Assert.Null(stored.ResearchAt);

        foreach (var (version, revises) in new (int?, bool)[] { (2, false), (3, true), (null, false) })
        {
            var later = Create();
            var again = await later.Service.EnsureAsync(later.Orders.Put(stored), version, revises, true, null, CancellationToken.None);
            Assert.Equal(ResearchJson.Serialize(result), ResearchJson.Serialize(again!));
            Assert.Empty(later.Search.Queries);
            Assert.Equal((0, 0), (later.Orders.Sets, later.Orders.Counts));
        }
    }

    [Fact]
    public async Task Stored_passages_are_never_used_for_an_order_with_files()
    {
        var first = Order(Clarify(IntakeRecordStatus.Ok, TopicA));
        var found = await Ensure(Create(), first);
        var order = first with { ResearchJson = ResearchJson.Serialize(found!) };
        var later = Create();

        var result = await Ensure(later, order, version: 2, files: true);

        Assert.Equal(ResearchStatus.Found, found!.Status);
        Assert.Equal(ResearchStatus.FilesAttached, result!.Status);
        Assert.Empty(result.Entries);
        Assert.Empty(later.Search.Queries);
        Assert.Equal(ResearchJson.Serialize(found), later.Orders.Find(order.Id)!.ResearchJson);
    }

    [Fact]
    public async Task An_order_with_files_but_without_topics_gets_nothing()
    {
        var s = Create();

        Assert.Null(await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok)), files: true));
        Assert.Empty(s.Search.Queries);
        Assert.Equal(0, s.Orders.Sets);
    }

    [Fact]
    public async Task A_topic_that_is_only_the_name_of_a_kind_of_gap_is_never_searched()
    {
        var s = Create();

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, "Facts", "other details", TopicA)));
        var none = await Ensure(Create(), Order(Clarify(IntakeRecordStatus.Ok, "Facts", "Date")));

        Assert.Equal([TopicA], s.Search.Queries);
        Assert.Equal([TopicA], result!.TopicsSent);
        Assert.Null(none);
    }

    [Fact]
    public async Task The_limit_per_account_stops_the_search_within_the_daily_limit()
    {
        var s = Create(On(o => o.DailyOrderCapPerUser = 1));

        var first = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));
        var second = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));
        var other = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA), user: "writer-b"));

        Assert.Equal([ResearchStatus.Found, ResearchStatus.DailyLimit, ResearchStatus.Found], new[] { first!.Status, second!.Status, other!.Status });
        Assert.Equal(2, s.Search.Queries.Count);
    }

    [Fact]
    public async Task Some_withheld_topics_leave_the_others_searched()
    {
        var s = Create();

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, "Firma Kranichsteiner Mindestlohn", TopicA)), profile: "Organization: Firma Kranichsteiner");

        Assert.Equal(ResearchStatus.Found, result!.Status);
        Assert.Equal(1, result.Withheld);
        Assert.Equal([TopicA], s.Search.Queries);
    }

    [Fact]
    public async Task More_topics_than_allowed_are_counted_over_the_limit()
    {
        var s = Create(On(o => o.MaxSearches = 1));

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB, "Mindestlohn Minijob")));

        Assert.Equal([TopicA], s.Search.Queries);
        Assert.Equal(2, result!.OverLimit);
    }

    [Fact]
    public async Task One_failed_search_among_successful_ones_is_counted_not_failed()
    {
        var s = Create(search: new FakeSearch(t => t == TopicB ? new([], "http_500", 3) : Hits(t)));

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB)));

        Assert.Equal(ResearchStatus.Found, result!.Status);
        Assert.Equal((2, 1), (result.Searches, result.SearchesFailed));
        Assert.Equal([QuoteA], result.Entries.Select(e => e.Quote));
    }

    [Theory]
    [InlineData("http_500", "http_502", "search_failed")]
    [InlineData("timeout", "timeout", "timeout")]
    [InlineData("timeout", "http", "search_failed")]
    public async Task When_every_search_fails_the_research_fails_without_the_picker(string codeA, string codeB, string expected)
    {
        var s = Create(search: new FakeSearch(t => new([], t == TopicA ? codeA : codeB, 3)));
        var order = Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB));

        var result = await Ensure(s, order);

        Assert.Equal((ResearchStatus.Failed, expected), (result!.Status, result.ErrorCode));
        Assert.Equal((2, 2), (result.Searches, result.SearchesFailed));
        Assert.Empty(s.Condenser.Calls);
        Assert.NotNull(s.Orders.Find(order.Id)!.ResearchJson);
        Assert.Contains(s.Log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Without_a_key_nothing_counts_as_sent()
    {
        var s = Create(search: new FakeSearch(_ => new([], TavilySearchClient.NoKeyCode, 0)));

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));

        Assert.Equal((ResearchStatus.Failed, OrderResearchService.NoKeyCode), (result!.Status, result.ErrorCode));
        Assert.Empty(result.TopicsSent);
        Assert.Equal((0, 0), (result.Searches, result.SearchesFailed));
    }

    [Fact]
    public async Task A_search_that_throws_counts_as_failed()
    {
        var s = Create(search: new FakeSearch(t => t == TopicA ? throw new InvalidOperationException("boom") : Hits(t)));

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB)));

        Assert.Equal(ResearchStatus.Found, result!.Status);
        Assert.Equal(1, result.SearchesFailed);
    }

    [Fact]
    public async Task Pages_without_raw_text_end_as_nothing_found_without_the_picker()
    {
        var s = Create(search: new FakeSearch(_ => new([], null, 3) { Skipped = 3 }));

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));

        Assert.Equal(ResearchStatus.NothingFound, result!.Status);
        Assert.Equal(0, result.Pages);
        Assert.Empty(s.Condenser.Calls);
        Assert.Contains(s.Log.Entries, e => e.Message.Contains("without raw text 3", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("condense_failed", "condense_failed")]
    [InlineData("timeout", "timeout")]
    [InlineData("something_else", "condense_failed")]
    public async Task A_failed_picker_ends_as_failed(string code, string expected)
    {
        var s = Create(condenser: new FakeCondenser((_, _) => new([], new Dictionary<ResearchDropReason, int>(), "m", code, 9)));

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));

        Assert.Equal((ResearchStatus.Failed, expected), (result!.Status, result.ErrorCode));
        Assert.Equal("m", result.Model);
        Assert.Equal(1, result.Pages);
    }

    [Fact]
    public async Task A_picker_that_throws_ends_as_failed()
    {
        var s = Create(condenser: new FakeCondenser((_, _) => throw new InvalidOperationException("boom")));

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));

        Assert.Equal((ResearchStatus.Failed, OrderResearchService.CondenseFailedCode), (result!.Status, result.ErrorCode));
    }

    [Fact]
    public async Task Invented_passages_end_as_nothing_found()
    {
        var s = Create(condenser: new FakeCondenser((_, pages) => new(
            [new(1, pages[0].Id, "Der Mindestlohn beträgt 20 Euro je Stunde ab sofort.")], new Dictionary<ResearchDropReason, int>(), "m", null, 4)));

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));

        Assert.Equal(ResearchStatus.NothingFound, result!.Status);
        Assert.Empty(result.Entries);
        Assert.Equal(1, result.Dropped[ResearchDropReason.NotInPage]);
    }

    [Fact]
    public async Task A_failing_daily_count_ends_as_failed_without_a_search()
    {
        var s = Create();
        s.Orders.CountFailure = new InvalidOperationException("database down");

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));

        Assert.Equal((ResearchStatus.Failed, OrderResearchService.ErrorCode), (result!.Status, result.ErrorCode));
        Assert.Empty(s.Search.Queries);
    }

    [Fact]
    public async Task A_failing_write_lets_the_run_go_on_without_the_passages_as_failed()
    {
        var s = Create();
        s.Orders.SetFailure = new InvalidOperationException("database down");

        var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));

        Assert.Equal((ResearchStatus.Failed, OrderResearchService.NotStoredCode), (result!.Status, result.ErrorCode));
        Assert.Empty(result.Entries);
        Assert.Contains(s.Log.Entries, e => e.Message.Contains("stored failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Whoever_loses_the_write_reads_the_stored_research()
    {
        var s = Create();
        var order = Order(Clarify(IntakeRecordStatus.Ok, TopicA));
        var winner = new OrderResearch(1, ResearchStatus.NothingFound, [TopicA], 0, 0, 1, 0, 1, 0, new Dictionary<ResearchDropReason, int>(), [], "other",
            12, null, new DateTimeOffset(2026, 11, 28, 9, 0, 0, TimeSpan.Zero));
        s.Orders.BeforeSet = id => s.Orders.Put(s.Orders.Find(id)! with { ResearchJson = ResearchJson.Serialize(winner), ResearchAt = winner.At });

        var result = await Ensure(s, order);

        Assert.Equal(ResearchJson.Serialize(winner), ResearchJson.Serialize(result!));
        Assert.Equal(1, s.Orders.Gets);
    }

    [Fact]
    public async Task Cancelling_the_caller_throws_and_stores_nothing()
    {
        var s = Create(search: new FakeSearch(Hits) { Delay = TimeSpan.FromSeconds(30) });
        var order = s.Orders.Put(Order(Clarify(IntakeRecordStatus.Ok, TopicA)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.Service.EnsureAsync(order, 1, false, false, null, cts.Token));
        Assert.Null(s.Orders.Find(order.Id)!.ResearchJson);
        Assert.NotNull(s.Orders.Find(order.Id)!.ResearchAt);
    }

    [Fact]
    public async Task Searches_run_side_by_side()
    {
        var s = Create(search: new FakeSearch(Hits) { Delay = TimeSpan.FromMilliseconds(400) });
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB, "Mindestlohn Minijob", "Mindestlohn Azubi")));

        Assert.Equal(4, s.Search.Queries.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(1_500), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task One_log_line_per_research_names_counts_but_no_contents()
    {
        var s = Create();

        await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB)));

        var line = Assert.Single(s.Log.Entries).Message;
        Assert.Contains("Found", line, StringComparison.Ordinal);
        Assert.Contains("entries 2", line, StringComparison.Ordinal);
        Assert.Contains("NotInPage:1", line, StringComparison.Ordinal);
        foreach (var secret in new[] { "Mindestlohn", "Stichtag", "example.org", "13,90", "Erhöhung", "Aushang" })
            Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_audit_is_written_only_with_a_directory_and_an_override()
    {
        var root = Path.Combine(Path.GetTempPath(), "research-audit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var plain = Path.Combine(root, "plain");
            await Ensure(Create(On(o => o.AuditDirectory = plain)), Order(Clarify(IntakeRecordStatus.Ok, TopicA)));
            Assert.False(Directory.Exists(plain));

            await Ensure(Create(On(o => o.ModeOverrides[User] = ResearchMode.On)), Order(Clarify(IntakeRecordStatus.Ok, TopicA)));

            var audit = Path.Combine(root, "audit");
            var s = Create(On(o =>
            {
                o.AuditDirectory = audit;
                o.ModeOverrides[User] = ResearchMode.On;
            }));
            var order = Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB));
            var result = await Ensure(s, order);

            var file = Assert.Single(Directory.GetFiles(audit));
            Assert.StartsWith(order.Id.ToString("N"), Path.GetFileName(file), StringComparison.Ordinal);
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(file));
            Assert.Equal(["orderId", "at", "status", "topicsSent", "pages", "entries", "dropped"], json.RootElement.EnumerateObject().Select(p => p.Name));
            Assert.Equal(["id", "topic", "url", "title", "rawText", "rawSha256"], json.RootElement.GetProperty("pages")[0].EnumerateObject().Select(p => p.Name));
            Assert.Equal(["id", "topic", "quote", "url", "title", "retrievedAt", "match", "rawSha256", "start", "length"],
                json.RootElement.GetProperty("entries")[0].EnumerateObject().Select(p => p.Name));
            var pages = json.RootElement.GetProperty("pages").EnumerateArray().ToList();
            Assert.Equal([PageA, PageB], pages.Select(p => p.GetProperty("rawText").GetString()));
            var entries = json.RootElement.GetProperty("entries").EnumerateArray().ToList();
            Assert.Equal(result!.Entries.Select(e => e.Quote), entries.Select(e => e.GetProperty("quote").GetString()));
            foreach (var entry in entries)
            {
                var page = pages.Single(p => p.GetProperty("rawSha256").GetString() == entry.GetProperty("rawSha256").GetString());
                var raw = page.GetProperty("rawText").GetString()!;
                Assert.Equal(entry.GetProperty("quote").GetString(),
                    raw.Substring(entry.GetProperty("start").GetInt32(), entry.GetProperty("length").GetInt32()));
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_failing_audit_never_stops_the_research()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "research-audit-file-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(blocker, "a file where the directory should be");
        try
        {
            var s = Create(On(o =>
            {
                o.AuditDirectory = blocker;
                o.ModeOverrides[User] = ResearchMode.On;
            }));

            var result = await Ensure(s, Order(Clarify(IntakeRecordStatus.Ok, TopicA)));

            Assert.Equal(ResearchStatus.Found, result!.Status);
            Assert.Contains(s.Log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("audit", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public async Task Only_the_topic_reaches_the_search_and_the_picker_sees_neither_request_nor_profile_nor_files()
    {
        const string RequestSentinel = "Zebrafink";
        const string ProfileSentinel = "Quastenflosser";
        const string FileSentinel = "Pfeilgiftfrosch";
        var tavily = new StubHttpHandler((request, body, _) =>
        {
            var query = JsonDocument.Parse(body).RootElement.GetProperty("query").GetString();
            var raw = query == TopicA ? PageA : PageB;
            return Task.FromResult(StubHttpHandler.Json($$$"""{"answer":"x","results":[{"url":"https://example.org/{{{(query == TopicA ? "lohn" : "stichtag")}}}","title":"T","content":"c","raw_content":{{{JsonSerializer.Serialize(raw)}}}}]}"""));
        });
        var llm = new StubHttpHandler((_, _, _) => Task.FromResult(StubHttpHandler.Completion(
            JsonSerializer.Serialize(new { passages = new[] { new { topic = 1, page = "P1", quote = QuoteA } } }))));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Tavily:Endpoint"] = "https://tavily.test.invalid",
            ["Tavily:ApiKey"] = "tvly-test",
            ["Research:Mode"] = "On",
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGroundingProviders(configuration);
        services.AddResearch(configuration);
        services.AddHttpClient(TavilySearchClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => tavily);
        var orders = new ResearchOrders();
        services.AddSingleton<Geef.Atelier.Core.Persistence.Orders.IOrderRepository>(orders);
        services.AddSingleton<Geef.Atelier.Application.Crew.Models.IModelAssignmentService>(new FixedAssignments("claude-cli", "claude-sonnet-latest"));
        services.AddSingleton<ILlmClientResolver>(new SingleClientResolver(new OpenAiCompatibleClient(new HttpClient(llm), "http://cli-proxy.test.invalid/v1/claude", "k")));
        await using var provider = services.BuildServiceProvider();
        var service = provider.CreateScope().ServiceProvider.GetRequiredService<Geef.Atelier.Application.Research.IOrderResearchService>();

        var order = orders.Put(Order(Clarify(IntakeRecordStatus.Ok, TopicA, TopicB, $"Mindestlohn {ProfileSentinel}")) with
        {
            RequestText = Request + " " + RequestSentinel,
        });
        var result = await service.EnsureAsync(order, 1, false, false, $"Name: {ProfileSentinel}\nAnything else: {FileSentinel}", CancellationToken.None);

        Assert.Equal(ResearchStatus.Found, result!.Status);
        Assert.Equal(1, result.Withheld);
        var queries = tavily.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement).ToList();
        Assert.Equal([TopicA, TopicB], queries.Select(q => q.GetProperty("query").GetString()).Order(StringComparer.Ordinal));
        foreach (var (request, body) in tavily.Requests)
        {
            Assert.Equal(["api_key", "include_answer", "include_raw_content", "max_results", "query", "search_depth"],
                JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            foreach (var sentinel in new[] { RequestSentinel, ProfileSentinel, FileSentinel, "Aushang", "Mitarbeitenden" })
            {
                Assert.DoesNotContain(sentinel, body, StringComparison.Ordinal);
                Assert.DoesNotContain(sentinel, request.RequestUri!.ToString(), StringComparison.Ordinal);
                Assert.DoesNotContain(request.Headers, h => h.Value.Any(v => v.Contains(sentinel, StringComparison.Ordinal)));
            }
        }

        var llmBody = Assert.Single(llm.Requests).Body;
        var messages = string.Join('\n', JsonDocument.Parse(llmBody).RootElement.GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("content").GetString()));
        foreach (var sentinel in new[] { RequestSentinel, ProfileSentinel, FileSentinel, "Aushang", "Mitarbeitenden" })
        {
            Assert.DoesNotContain(sentinel, llmBody, StringComparison.Ordinal);
            Assert.DoesNotContain(sentinel, messages, StringComparison.Ordinal);
        }
        Assert.Contains("1. Mindestlohn 2026 Höhe\n2. Mindestlohn Stichtag Erhöhung\n</topics>", messages, StringComparison.Ordinal);
        Assert.Equal(QuoteA, Assert.Single(result.Entries).Quote);
    }
}
