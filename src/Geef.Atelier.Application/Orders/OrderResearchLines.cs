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

    public const string NothingFound = "Some facts were looked up on the web, but no page gave a usable passage. The text does not rely on the web.";
    public const string Failed = "Looking things up on the web did not work this time. The text was written without it.";
    public const string DailyLimit = "Nothing was looked up on the web today because the daily limit was reached. The text was written without it.";
    public const string AllWithheld = "Nothing was looked up on the web, because the search would have contained details from your files or your About me details.";
    public const string SomeWithheld = "Some details were not looked up, because the search would have contained details from your files or your About me details.";

    public const string FactsComparedLeadLookedUp = "Facts in the text were compared with your request, your answers, your files and the pages looked up.";
    public const string FactsComparedLeadLookedUpNoFiles = "Facts in the text were compared with your request, your answers and the pages looked up.";

    public const string NotGivenLeadLookedUp = "You did not give these facts, so they were not assumed. Where the pages looked up cover one, the text may use it:";

    /// <summary>Origin of a passage of the material found in the research, in the sentence about a contradiction.</summary>
    public const string FromLookedUp = "the pages looked up";

    /// <summary>
    /// The research as the person sees it: <c>null</c> without research, for <see cref="ResearchStatus.NotNeeded"/>, and for found entries
    /// when the delivered run did not use them (<paramref name="deliveredRunLookedUp"/>); every other status shows its sentence.
    /// </summary>
    public static OrderLookedUpView? Build(OrderResearch? research, bool deliveredRunLookedUp) => throw new NotImplementedException();
}
