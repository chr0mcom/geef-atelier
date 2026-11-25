using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Core.Domain.Research;

/// <summary>How the research of an order ended.</summary>
public enum ResearchStatus { NotNeeded = 0, Found = 1, NothingFound = 2, Failed = 3, DailyLimit = 4, AllWithheld = 5 }

/// <summary>Why a passage proposed by the model or by the parser did not become an entry; the first matching rule decides.</summary>
public enum ResearchDropReason { NotInPage = 0, TooShort = 1, Unsafe = 2, Duplicate = 3, OverLimit = 4, UnknownPage = 5, OverlongQuote = 6 }

/// <summary>A search result with its raw page text, already cut to the configured length; <see cref="Id"/> is <c>P1…</c>.</summary>
public sealed record ResearchPage(string Id, int Topic, string Url, string Title, string RawText);

/// <summary>A passage as the model proposed it, before the code looked for it in the page.</summary>
public sealed record ProposedPassage(int Topic, string PageId, string Quote);

/// <summary>
/// A passage found word for word in the raw text of its page. <see cref="Quote"/> is the original wording of the page, never the
/// model's text; <see cref="Id"/> is <c>W1.1…</c>; <see cref="Start"/> and <see cref="Length"/> locate it in the raw text whose
/// SHA-256 is <see cref="RawSha256"/>.
/// </summary>
public sealed record ResearchEntry(string Id, int Topic, string Quote, string Url, string Title, DateTimeOffset RetrievedAt,
    QuoteMatchKind Match, string RawSha256, int Start, int Length);

/// <summary>The research of an order, stored as JSON in <c>Orders.ResearchJson</c> and used unchanged by every run of the order.</summary>
public sealed record OrderResearch(int SchemaVersion, ResearchStatus Status, IReadOnlyList<string> TopicsSent, int Withheld, int OverLimit,
    int Searches, int SearchesFailed, int Pages, int Proposed, IReadOnlyDictionary<ResearchDropReason, int> Dropped,
    IReadOnlyList<ResearchEntry> Entries, string? Model, int DurationMs, string? ErrorCode, DateTimeOffset At)
{
    public const int CurrentSchemaVersion = 1;
}

/// <summary>JSON form (camelCase, enums as text) of <see cref="OrderResearch"/>.</summary>
public static class ResearchJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes <paramref name="research"/>.</summary>
    public static string Serialize(OrderResearch research)
    {
        ArgumentNullException.ThrowIfNull(research);
        return JsonSerializer.Serialize(research, JsonOptions);
    }

    /// <summary>
    /// Parses stored research; <c>null</c> for null, blank or unreadable JSON. Unknown fields are ignored, missing lists become empty,
    /// and entries without a passage are left out.
    /// </summary>
    public static OrderResearch? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        OrderResearch? research;
        try
        {
            research = JsonSerializer.Deserialize<OrderResearch>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (research is null)
            return null;

        return research with
        {
            TopicsSent = (research.TopicsSent ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            Dropped = research.Dropped ?? new Dictionary<ResearchDropReason, int>(),
            Entries = (research.Entries ?? [])
                .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Quote))
                .Select(e => e with
                {
                    Id = e.Id ?? string.Empty,
                    Url = e.Url ?? string.Empty,
                    Title = e.Title ?? string.Empty,
                    RawSha256 = e.RawSha256 ?? string.Empty,
                })
                .ToList(),
        };
    }
}
