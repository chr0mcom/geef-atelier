using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Domain.FactCheck;

/// <summary>
/// <see cref="FactCarryOver.Merge"/>: findings of earlier answered rounds stay open in a later wording when their passage still
/// stands there (with their blocking effect), source conflicts always; only a passage wholly inside a kept one of the same kind
/// (and the same conflict at word boundaries in the same files) is dropped, nothing is capped, ids anew.
/// </summary>
public sealed class FactCarryOverTests
{
    private const string Text = "Liebe Eltern, der Elternabend ist am 14. März. Bitte geben Sie uns in der Gruppe Bescheid. Der Eintritt kostet 5 Euro pro Person.";

    private static FactFinding Passage(string id, string quote, bool blocking = true, FactFindingKind kind = FactFindingKind.Unsupported, string? instruction = null) =>
        new(id, kind, quote, "x", instruction, null, null, null, kind == FactFindingKind.Instruction ? InstructionOrigin.Stated : null, blocking, QuoteMatchKind.Exact);

    private static FactFinding Conflict(string id, string first, string second, string? quote = null, string file = "notizen.txt") =>
        new(id, FactFindingKind.SourceConflict, quote, "x", null, first, second, file, null, false, quote is null ? QuoteMatchKind.NotFound : QuoteMatchKind.Exact);

    private const string Fee = "Liebe Eltern, der Beitrag beträgt 25 Euro und ist bis Freitag in bar zu zahlen. Bitte bringen Sie Hausschuhe mit.";

    [Fact]
    public void An_earlier_passage_is_kept_beside_a_shorter_one_flagged_later_inside_it()
    {
        var earlier = new[] { Passage("f1", "der Beitrag beträgt 25 Euro und ist bis Freitag in bar zu zahlen.") };
        var fresh = new[] { Passage("f1", "in bar zu zahlen.", blocking: false) };

        var merged = FactCarryOver.Merge(fresh, [earlier], Fee);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, f => f.TextQuote == "in bar zu zahlen." && !f.Blocking);
        Assert.Contains(merged, f => f.TextQuote!.Contains("25 Euro", StringComparison.Ordinal) && f.Blocking);
    }

    [Fact]
    public void An_earlier_passage_that_only_overlaps_a_later_one_is_kept_beside_it()
    {
        var earlier = new[] { Passage("f1", "der Beitrag beträgt 25 Euro und ist bis Freitag") };
        var fresh = new[] { Passage("f1", "bis Freitag in bar zu zahlen. Bitte bringen Sie Hausschuhe mit.") };

        var merged = FactCarryOver.Merge(fresh, [earlier], Fee);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, f => f.TextQuote!.Contains("25 Euro", StringComparison.Ordinal));
    }

    [Fact]
    public void An_earlier_passage_against_another_instruction_is_kept_although_a_kept_one_contains_it()
    {
        var fresh = new[] { Passage("f1", "der Beitrag beträgt 25 Euro und ist bis Freitag in bar zu zahlen.", kind: FactFindingKind.Instruction, instruction: "ohne Beträge") };
        var other = new[] { Passage("f1", "in bar zu zahlen.", kind: FactFindingKind.Instruction, instruction: "per Überweisung") };
        var same = new[] { Passage("f1", "in bar zu zahlen.", kind: FactFindingKind.Instruction, instruction: "Ohne Beträge") };

        Assert.Equal(2, FactCarryOver.Merge(fresh, [other], Fee).Count);
        Assert.Single(FactCarryOver.Merge(fresh, [same], Fee));
    }

    [Fact]
    public void An_earlier_conflict_counts_as_the_same_only_at_word_boundaries_and_in_the_same_file()
    {
        var fresh = new[] { Conflict("f1", "Abgabe 12. Mai", "Abgabe 15. Mai") };

        Assert.Equal(2, FactCarryOver.Merge(fresh, [[Conflict("f1", "2. Mai", "5. Mai")]], Text).Count);
        Assert.Equal(2, FactCarryOver.Merge(fresh, [[Conflict("f1", "Abgabe 12. Mai", "Abgabe 15. Mai", file: "plan.txt")]], Text).Count);
        Assert.Single(FactCarryOver.Merge(fresh, [[Conflict("f1", "12. Mai", "15. Mai")]], Text));
    }

    [Fact]
    public void An_earlier_passage_that_still_stands_is_kept_with_its_blocking_and_a_source_conflict_always()
    {
        var earlier = new[] { Passage("f1", "Bitte geben Sie uns in der Gruppe Bescheid."), Conflict("f2", "Summe 62 €", "Farbe 19 €") };

        var merged = FactCarryOver.Merge([], [earlier], Text);

        Assert.Equal(["f1", "f2"], merged.Select(f => f.Id));
        Assert.True(merged[0].Blocking);
        Assert.Equal("Bitte geben Sie uns in der Gruppe Bescheid.", merged[0].TextQuote);
        Assert.Equal(FactFindingKind.SourceConflict, merged[1].Kind);
    }

    [Fact]
    public void An_earlier_passage_that_is_gone_is_not_kept_and_a_source_conflict_loses_only_its_passage()
    {
        var earlier = new[] { Passage("f1", "Wir treffen uns im Raum 12."), Conflict("f2", "Summe 62 €", "Farbe 19 €", "Wir treffen uns im Raum 12.") };

        var merged = FactCarryOver.Merge([], [earlier], Text);

        var conflict = Assert.Single(merged);
        Assert.Equal((FactFindingKind.SourceConflict, (string?)null, QuoteMatchKind.NotFound), (conflict.Kind, conflict.TextQuote, conflict.Match));
    }

    [Fact]
    public void A_passage_that_only_continues_a_word_or_number_does_not_count_as_still_there()
    {
        var earlier = new[] { Passage("f1", "Der Eintritt kostet 5 Euro pro Person.") };

        Assert.Empty(FactCarryOver.Merge([], [earlier], Text.Replace("5 Euro", "15 Euro", StringComparison.Ordinal)));
        Assert.Single(FactCarryOver.Merge([], [earlier], Text));
    }

    [Fact]
    public void Only_a_real_overlap_is_dropped_and_then_blocks_when_either_blocks()
    {
        var fresh = new[] { Passage("f1", "Bitte geben Sie uns in der Gruppe Bescheid", blocking: false) };
        var earlier = new[]
        {
            Passage("f1", "Bitte geben Sie uns in der Gruppe Bescheid."),
            Passage("f2", "Bitte geben Sie uns in der Gruppe Bescheid.", kind: FactFindingKind.Instruction, instruction: "ohne Emojis"),
            Conflict("f3", "Summe 62 €", "Farbe 19 €"),
            Conflict("f4", "Farbe 19 €", "Summe 62 €"),
            Conflict("f5", "Summe 62 €", "Papier 31 €"),
        };

        var merged = FactCarryOver.Merge(fresh, [earlier], Text);

        Assert.Equal(4, merged.Count);
        Assert.True(Assert.Single(merged, f => f.Kind == FactFindingKind.Unsupported).Blocking);
        Assert.Single(merged, f => f.Kind == FactFindingKind.Instruction);
        Assert.Equal(2, merged.Count(f => f.Kind == FactFindingKind.SourceConflict));
        Assert.Equal(["f1", "f2", "f3", "f4"], merged.Select(f => f.Id));
    }

    [Fact]
    public void Findings_of_every_earlier_round_are_kept_and_the_latest_version_of_a_finding_wins()
    {
        var round1 = new[] { Passage("f1", "der Elternabend ist am 14. März") };
        var round2 = new[] { Passage("f1", "Der Eintritt kostet 5 Euro pro Person."), Passage("f2", "Liebe Eltern, der Elternabend ist am 14. März", blocking: false) };

        var merged = FactCarryOver.Merge([], [round2, round1], Text);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, f => f.TextQuote == "Der Eintritt kostet 5 Euro pro Person.");
        Assert.True(Assert.Single(merged, f => f.TextQuote!.Contains("März", StringComparison.Ordinal)).Blocking);
    }

    [Fact]
    public void No_limit_drops_a_finding()
    {
        var sentences = Enumerable.Range(1, 20).Select(i => $"Satz Nummer {i} steht so nicht im Material.").ToList();
        var text = string.Join(" ", sentences);

        var merged = FactCarryOver.Merge(sentences.Take(10).Select((s, i) => Passage($"f{i + 1}", s)).ToList(), [sentences.Skip(10).Select((s, i) => Passage($"f{i + 1}", s)).ToList()], text);

        Assert.Equal(20, merged.Count);
        Assert.Equal(20, merged.Select(f => f.Id).Distinct().Count());
    }
}
