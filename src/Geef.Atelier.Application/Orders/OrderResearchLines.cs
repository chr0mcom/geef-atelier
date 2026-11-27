using System.Globalization;
using Geef.Atelier.Core.Domain.Research;

namespace Geef.Atelier.Application.Orders;

/// <summary>
/// Every sentence the person reads about the research of an order, on the web and over MCP alike (contract stage 4, 4.7). No other
/// class words these sentences.
/// </summary>
public static class OrderResearchLines
{
    public const string Heading = "Looked up";
    public const string Subline = "Passages copied word for word from the pages below. They show what the pages say – not that it is true or that the text uses it.";

    /// <summary>Line of an entry; <c>{0}</c> is the day it was retrieved, written with <see cref="RetrievedDateFormat"/>.</summary>
    public const string RetrievedFormat = "retrieved {0}";
    public const string RetrievedDateFormat = "d MMM yyyy";

    /// <summary>
    /// What goes to the web search service, in the same words on every page that describes it (explanation page, start page, hosting
    /// page). It promises nothing the code does not hold: the filter tries, the About me comparison can miss a reworded detail.
    /// </summary>
    public const string SearchPrivacy = "Atelier tries to leave out email addresses, phone numbers, account numbers and street addresses that it recognizes. It does not recognize every form, and names or other personal details in your request can end up in the search words. Your answers and files are never sent. Words from your About me details are held back unless you also wrote them in your request, but a translated or reworded detail can slip through. The result shows which search words were sent.";

    /// <summary>Lead of the search words that went to the search service, shown only to the person who placed the order.</summary>
    public const string SearchWordsLead = "These search words went to a web search service. They come from your request:";

    public const string NothingFound = "Some facts were looked up on the web, but no page gave a usable passage. The text does not rely on the web.";
    public const string Failed = "Looking things up on the web did not work this time. The text was written without it.";
    public const string DailyLimit = "Nothing was looked up on the web today because the daily limit was reached. The text was written without it.";
    /// <summary>
    /// Said when every topic was withheld. Neutral on purpose: a topic is withheld for a word of the About me details, for a topic in
    /// another language than the order, for a link, an email address, a phone or account number, a date of birth or a street address,
    /// or for access details, and only the number is stored. It never claims that the words that did go out held no personal details.
    /// </summary>
    public const string AllWithheld = "Nothing was looked up on the web: Atelier held back every search word, for example because it held an address, a number or a word from your About me details.";
    public const string SomeWithheld = "Some search words were held back and not looked up, for example because they held an address, a number or a word from your About me details.";
    public const string FilesAttached = "No web search was done because you attached files, so nothing from them can reach a search engine.";

    public const string FactsComparedLeadLookedUp = "Facts in the text were compared with your request, your answers, your files and the pages looked up.";
    public const string FactsComparedLeadLookedUpNoFiles = "Facts in the text were compared with your request, your answers and the pages looked up.";

    public const string NotGivenLeadLookedUp = "You did not give these facts, so they were not assumed. Where the pages looked up cover one, the text may use it:";

    /// <summary>Said with a passage of the text that the pages looked up say otherwise; a hint, never a matter of the person's files.</summary>
    public const string DiffersFromLookedUp = "The pages looked up say otherwise – please check which is right.";

    /// <summary>Origin of a passage of the material found in the research, in the sentence about a contradiction.</summary>
    public const string FromLookedUp = "the pages looked up";

    /// <summary>
    /// The research as the person sees it: <c>null</c> without research, for <see cref="ResearchStatus.NotNeeded"/>, and for found entries
    /// when the delivered run did not use them (<paramref name="deliveredRunLookedUp"/>); every other status shows its sentence. The title
    /// of an entry is <see cref="ResearchAddress.SafeTitle"/>, also for research stored before that rule. Found
    /// research without an entry shows the sentence of <see cref="ResearchStatus.NothingFound"/>, so that it never stays silent; topics
    /// that were withheld add <see cref="SomeWithheld"/> to entries and to that sentence. With <paramref name="showSearchWords"/> (only
    /// for the person who placed the order) the view also lists the topics that went to the search service, always when there are any:
    /// also without a text (<paramref name="hasText"/> false; then without the sentence, which speaks of the text) and when the delivered
    /// run did not use the found passages (then with <see cref="NotUsedForThisText"/> instead of the passages). Without a text and
    /// without search words to show there is no view.
    /// </summary>
    public static OrderLookedUpView? Build(OrderResearch? research, bool deliveredRunLookedUp, bool showSearchWords = false, bool hasText = true)
    {
        var words = showSearchWords ? (research?.TopicsSent ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList() : [];
        if (research is null || research.Status == ResearchStatus.NotNeeded)
            return null;
        if (!hasText)
            return words.Count == 0 ? null : new OrderLookedUpView(research.Status, null, Math.Max(0, research.Withheld), [], words);
        var view = BuildView(research, deliveredRunLookedUp);
        if (view is null)
            return words.Count == 0 ? null : new OrderLookedUpView(research.Status, NotUsedForThisText, Math.Max(0, research.Withheld), [], words);
        return words.Count == 0 ? view : view with { SearchWords = words };
    }

    /// <summary>Said beside the search words when passages were found but the shown text was written without them.</summary>
    public const string NotUsedForThisText = "Pages were looked up, but this version of the text was written without them.";

    private static OrderLookedUpView? BuildView(OrderResearch? research, bool deliveredRunLookedUp)
    {
        if (research is null || research.Status == ResearchStatus.NotNeeded)
            return null;

        var entries = (research.Entries ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e.Quote))
            .Select(e => new OrderLookedUpEntry(e.Quote.Trim(), WebAddress(e.Url), ResearchAddress.SafeTitle(e.Title, e.Url), e.RetrievedAt))
            .ToList();
        var withheld = Math.Max(0, research.Withheld);
        var some = withheld > 0 ? SomeWithheld : null;

        return research.Status switch
        {
            ResearchStatus.Found when entries.Count > 0 => deliveredRunLookedUp ? new OrderLookedUpView(ResearchStatus.Found, some, withheld, entries) : null,
            ResearchStatus.Found or ResearchStatus.NothingFound =>
                new OrderLookedUpView(ResearchStatus.NothingFound, Join(NothingFound, some), withheld, []),
            ResearchStatus.Failed => new OrderLookedUpView(ResearchStatus.Failed, Failed, withheld, []),
            ResearchStatus.DailyLimit => new OrderLookedUpView(ResearchStatus.DailyLimit, DailyLimit, withheld, []),
            ResearchStatus.AllWithheld => new OrderLookedUpView(ResearchStatus.AllWithheld, AllWithheld, withheld, []),
            ResearchStatus.FilesAttached => new OrderLookedUpView(ResearchStatus.FilesAttached, FilesAttached, 0, []),
            _ => null,
        };
    }

    /// <summary>The line of an entry: <see cref="RetrievedFormat"/> with the day (UTC) written as <see cref="RetrievedDateFormat"/>.</summary>
    public static string Retrieved(DateTimeOffset retrievedAt) =>
        string.Format(CultureInfo.InvariantCulture, RetrievedFormat, retrievedAt.UtcDateTime.ToString(RetrievedDateFormat, CultureInfo.InvariantCulture));

    /// <summary>The host of an address kept by <see cref="WebAddress"/>, without a leading <c>www.</c>; <c>null</c> for anything else.</summary>
    public static string? Host(string? url) => ResearchAddress.Host(url);

    /// <summary>
    /// <paramref name="url"/> trimmed when it is an absolute http or https address with a host, without user information and at most
    /// <see cref="ResearchAddress.MaxChars"/> characters long (<see cref="ResearchAddress.Acceptable"/>); otherwise <c>null</c>, so it is never a link.
    /// </summary>
    public static string? WebAddress(string? url) => ResearchAddress.Acceptable(url);

    private static string Join(string first, string? second) => second is null ? first : first + " " + second;
}
