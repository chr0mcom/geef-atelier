using System.Text.Json;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Tools.FactCheckEval;

namespace Geef.Atelier.Tests.FactCheck;

/// <summary>A synthetic, complete fact-check corpus and a kit whose parser and filter are simple stand-ins for the production code.</summary>
internal static class FactCheckTestData
{
    public const string ContactSentence = "Fragen bitte an die Nummer 0151 22334455 richten.";
    public const string ChangedDate = "Der Ausflug ist am Mittwoch, dem 7. Mai.";
    public const string WrongTotalLine = "Gesamt pro Kind: 17 €";

    /// <summary>16 bases: 15 ordinary ones (each with a planted changed date) and the parents' evening, with at least 3 held-out bases.</summary>
    public static IReadOnlyList<CorpusBasis> Bases()
    {
        var bases = new List<CorpusBasis>();
        for (var i = 1; bases.Count < 15; i++)
        {
            var request = $"Synthetischer Auftrag Nummer {i}";
            bases.Add(Basis($"fc-{bases.Count + 1:00}", request, null, "plain", null, null));
        }
        bases.Add(Basis("fc-16", "Elternbrief aus Notizen", FactCheckCorpus.ParentsEvening, FactCheckCorpus.FaultySourceKind,
            [new SourceError("wrong_total", WrongTotalLine, null, "17")],
            [new CorpusPlant("changed_date", ChangedDate), new CorpusPlant(FactCheckCorpus.InventedContact, ContactSentence)]));
        if (bases.Count(b => FactCheckCorpus.SplitOf(b) == EvalBasics.Holdout) < 3)
            throw new InvalidOperationException("the synthetic corpus needs at least three held-out bases");
        return bases;
    }

    public static CorpusBasis Basis(string id, string request, string? @case, string kind, IReadOnlyList<SourceError>? errors, IReadOnlyList<CorpusPlant>? plants)
    {
        var clean = "Liebe Eltern, der Ausflug ist am Dienstag. Bitte zahlen Sie bis Ende April.";
        var contact = plants?.Any(p => p.Kind == FactCheckCorpus.InventedContact) == true ? $" {ContactSentence}" : string.Empty;
        var planted = $"Liebe Eltern. {ChangedDate} Bitte zahlen Sie bis Ende April.{contact}";
        return new CorpusBasis(id, "short", "de", kind, @case, request, [], null,
            [new CorpusFile("S1", "notizen.txt", $"Ausflug am Dienstag.\nBusfahrt 9 €\nEintritt 6 €\nWorkshop 4 €\n{WrongTotalLine}\n")],
            errors, clean, [], planted, plants ?? [new CorpusPlant("changed_date", ChangedDate)]);
    }

    /// <summary>
    /// A kit with a readable stand-in answer format: <c>{"findings":[{"kind":"unsupported","text_quote":"…"}]}</c>; every proposed
    /// finding is kept, blocking unless it is a source conflict; quotes are found by ordinal search.
    /// </summary>
    public static FactCheckKit Kit(Func<IReadOnlyList<(string SourceId, string FileName, string Text)>, SourceArithmeticResult>? arithmetic = null) => new(
        "system", "check_facts", "{}",
        (material, draft) => material.Request + "\n" + draft,
        Parse,
        Filter,
        (haystack, quote) => haystack.IndexOf(quote, StringComparison.Ordinal) is var i and >= 0 ? (i, quote.Length) : null,
        arithmetic ?? (_ => throw new NotImplementedException()));

    public static string Answer(params (string Kind, string? TextQuote, string? MaterialQuote)[] findings) =>
        JsonSerializer.Serialize(new { findings = findings.Select(f => new { kind = f.Kind, text_quote = f.TextQuote, material_quote = f.MaterialQuote }) });

    public static FactCheckLine Line(FactCheckItem item, FactCheckKit kit, string status = CallStatus.Ok, string? raw = null, int durationMs = 20_000, string? hash = null) =>
        new(item.Id, item.Split, status, "gpt-astra-latest", durationMs, status == CallStatus.Ok ? null : "x",
            status == CallStatus.Ok ? raw ?? Answer() : null, 180_000, null, "fact-check-1", hash ?? kit.InputHash(item), DateTimeOffset.UnixEpoch);

    private static IReadOnlyList<ProposedFactFinding>? Parse(string? raw)
    {
        if (raw is null || raw == "unreadable")
            return null;
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("findings").EnumerateArray().Select(f => new ProposedFactFinding(
            f.GetProperty("kind").GetString() switch
            {
                "source_conflict" => FactFindingKind.SourceConflict,
                "superseded" => FactFindingKind.Superseded,
                "instruction" => FactFindingKind.Instruction,
                _ => FactFindingKind.Unsupported,
            },
            f.GetProperty("text_quote").GetString(), "why", null, f.GetProperty("material_quote").GetString(), null)).ToList();
    }

    private static FactFilterResult Filter(IReadOnlyList<ProposedFactFinding> proposed, string draft, FactMaterial material) => new(
        proposed.Select((p, i) => new FactFinding($"f{i + 1}", p.Kind, p.TextQuote, p.WhatIsWrong, p.InstructionQuote, p.MaterialQuote, p.MaterialQuote2,
            null, null, p.Kind != FactFindingKind.SourceConflict, QuoteMatchKind.Exact)).ToList(),
        proposed.Select((_, i) => new FactFilterTrace(i, FactFilterDecision.Kept)).ToList());
}
