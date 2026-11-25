using System.Text.Json.Serialization;
using Geef.Atelier.Tools.FactCheckEval;
using Geef.Atelier.Tools.IntakeEval;
using CorpusProfile = Geef.Atelier.Tools.IntakeEval.CorpusProfile;

namespace Geef.Atelier.Tools.ResearchEval;

/// <summary>
/// One order of measurement VR: the synthetic corpus entry with the research topics the stored question gate kept
/// (<c>results-20261125.jsonl</c>, contract stage 4, 2.6). No clarifier is called again.
/// </summary>
public sealed record ResearchOrder(string Id, string Split, string Request, IReadOnlyList<string> Topics, IReadOnlyList<string> FileTexts, string? ProfileText);

/// <summary>The fields of a line of the stored gate output of the intake measurement (<c>results-20261125.jsonl</c>) that VR reads.</summary>
public sealed record StoredGateLine(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("split")] string Split,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("research_topics")] IReadOnlyList<string>? ResearchTopics);

/// <summary>
/// The fixed order sets of measurement VR (contract stage 4, 3.3): of the 36 orders with topics, the development set is the first
/// 12 development orders by id, the held-out set all 9 held-out orders. The other 15 development orders are reserved for R3 and are
/// never part of a set.
/// </summary>
public static class ResearchCorpus
{
    /// <summary>Name of the development set of VR (12 orders).</summary>
    public const string DevSet = "dev";

    /// <summary>Name of the held-out set of VR (9 orders).</summary>
    public const string HoldoutSet = "holdout";

    /// <summary>Orders with research topics in the stored gate output.</summary>
    public const int OrdersWithTopics = 36;

    /// <summary>Of them in the development split.</summary>
    public const int DevOrders = 27;

    /// <summary>Of them in the held-out split.</summary>
    public const int HoldoutOrders = 9;

    /// <summary>Development orders measured in VR.</summary>
    public const int DevSetSize = 12;

    /// <summary>
    /// The orders with research topics, sorted by id, and what does not match the contract (another number of orders, an order
    /// without corpus entry, an id twice). With a problem no set may be measured.
    /// </summary>
    public static (IReadOnlyList<ResearchOrder> Orders, IReadOnlyList<string> Problems) Load(IReadOnlyList<StoredGateLine> intake, IReadOnlyList<CorpusEntry> corpus)
    {
        var problems = new List<string>();
        var entries = new Dictionary<string, CorpusEntry>(StringComparer.Ordinal);
        foreach (var entry in corpus)
        {
            if (!entries.TryAdd(entry.Id, entry))
                problems.Add($"corpus id {entry.Id} occurs twice");
        }

        var withTopics = intake.Where(l => l.ResearchTopics is { Count: > 0 }).ToList();
        foreach (var twice in withTopics.GroupBy(l => l.Id, StringComparer.Ordinal).Where(g => g.Count() > 1))
            problems.Add($"intake result {twice.Key} occurs {twice.Count()} times");

        var orders = new List<ResearchOrder>();
        foreach (var line in withTopics.DistinctBy(l => l.Id, StringComparer.Ordinal).OrderBy(l => l.Id, StringComparer.Ordinal))
        {
            if (line.Status != "Ok")
                problems.Add($"intake result {line.Id} has topics but status {line.Status}");
            if (!entries.TryGetValue(line.Id, out var entry))
            {
                problems.Add($"intake result {line.Id} has no corpus entry");
                continue;
            }
            orders.Add(new ResearchOrder(line.Id, line.Split, entry.Request, line.ResearchTopics!.ToList(),
                (entry.Files ?? []).Select(f => f.Text).ToList(), ProfileText(entry.Profile)));
        }

        Expect(problems, "orders with research topics", orders.Count, OrdersWithTopics);
        Expect(problems, "development orders with topics", orders.Count(o => o.Split == EvalBasics.Dev), DevOrders);
        Expect(problems, "held-out orders with topics", orders.Count(o => o.Split == EvalBasics.Holdout), HoldoutOrders);
        return (orders, problems);
    }

    /// <summary>The orders of <paramref name="set"/> (<see cref="DevSet"/> or <see cref="HoldoutSet"/>) in id order.</summary>
    public static IReadOnlyList<ResearchOrder> Set(IReadOnlyList<ResearchOrder> orders, string set) => set switch
    {
        DevSet => orders.Where(o => o.Split == EvalBasics.Dev).OrderBy(o => o.Id, StringComparer.Ordinal).Take(DevSetSize).ToList(),
        HoldoutSet => orders.Where(o => o.Split == EvalBasics.Holdout).OrderBy(o => o.Id, StringComparer.Ordinal).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(set), set, "unknown set"),
    };

    /// <summary>The development orders kept untouched for R3.</summary>
    public static IReadOnlyList<ResearchOrder> Reserved(IReadOnlyList<ResearchOrder> orders)
    {
        var measured = Set(orders, DevSet).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        return orders.Where(o => o.Split == EvalBasics.Dev && !measured.Contains(o.Id)).ToList();
    }

    /// <summary>The set an order belongs to, or null for a reserved order.</summary>
    public static string? SetOf(IReadOnlyList<ResearchOrder> orders, string id) =>
        Set(orders, DevSet).Any(o => o.Id == id) ? DevSet : Set(orders, HoldoutSet).Any(o => o.Id == id) ? HoldoutSet : null;

    /// <summary>
    /// All fields of the About me details joined by line breaks; a superset of what production hands the query gate, so the
    /// measurement never sends a word the gate would withhold there.
    /// </summary>
    public static string? ProfileText(CorpusProfile? profile)
    {
        if (profile is null)
            return null;
        var parts = new[] { profile.DisplayName, profile.Role, profile.Organization, profile.Formality, profile.Language, profile.Signature, profile.NoGos, profile.FreeText }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        return parts.Count == 0 ? null : string.Join('\n', parts);
    }

    private static void Expect(List<string> problems, string what, int actual, int expected)
    {
        if (actual != expected)
            problems.Add($"{actual} {what}, the contract expects {expected}");
    }
}
