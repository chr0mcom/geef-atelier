using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Domain.FactCheck;

public sealed class FactRecheckTests
{
    private static FactFinding Finding(string id, string? quote, FactFindingKind kind = FactFindingKind.Unsupported, bool blocking = true) =>
        new(id, kind, quote, "Not backed.", null, null, null, null, null, blocking, QuoteMatchKind.Exact);

    private const string Revised = "Sehr geehrte Damen und Herren,\n\ndas Gerät kam beschädigt an. Fotos des Schadens liegen bei.\n\nMit freundlichen Grüßen";

    [Fact]
    public void Quote_still_verbatim_stays_open_without_a_call()
    {
        var (stillThere, toAsk) = FactRecheck.Presort([Finding("f1", "Fotos des Schadens liegen bei.")], Revised);

        var open = Assert.Single(stillThere);
        Assert.True(open.Blocking);
        Assert.Empty(toAsk);
    }

    [Fact]
    public void Quote_still_there_after_normalization_stays_open()
    {
        var (stillThere, _) = FactRecheck.Presort([Finding("f1", "fotos des schadens liegen bei")], Revised);

        Assert.Equal(QuoteMatchKind.Normalized, Assert.Single(stillThere).Match);
    }

    [Fact]
    public void Quote_gone_or_only_similar_must_be_asked()
    {
        var (stillThere, toAsk) = FactRecheck.Presort(
            [Finding("f1", "Die Rechnung liegt diesem Schreiben bei."), Finding("f2", "Fotos des Schadens liegen anbei vor.")], Revised);

        Assert.Empty(stillThere);
        Assert.Equal(["f1", "f2"], toAsk.Select(f => f.Id));
    }

    [Fact]
    public void Source_conflict_stays_open_without_a_call()
    {
        var conflict = Finding("f3", null, FactFindingKind.SourceConflict, blocking: false);

        var (stillThere, toAsk) = FactRecheck.Presort([conflict], Revised);

        Assert.Equal(conflict, Assert.Single(stillThere));
        Assert.Empty(toAsk);
    }

    [Fact]
    public void Fixed_findings_are_no_longer_open()
    {
        var open = FactRecheck.Resolve([Finding("f1", "Die Rechnung liegt bei.")], [new RecheckResult("f1", RecheckStatus.Fixed, null)], Revised);

        Assert.Empty(open);
    }

    [Fact]
    public void Still_there_with_a_verified_quote_stays_blocking()
    {
        var open = FactRecheck.Resolve([Finding("f1", "Die Rechnung liegt bei.")],
            [new RecheckResult("f1", RecheckStatus.StillThere, "Fotos des Schadens liegen bei")], Revised);

        var finding = Assert.Single(open);
        Assert.True(finding.Blocking);
        Assert.Equal("Fotos des Schadens liegen bei", finding.TextQuote);
    }

    [Fact]
    public void Still_there_keeps_a_hint_non_blocking()
    {
        var open = FactRecheck.Resolve([Finding("f1", "Die Rechnung liegt bei.", FactFindingKind.Instruction, blocking: false)],
            [new RecheckResult("f1", RecheckStatus.StillThere, "Fotos des Schadens liegen bei")], Revised);

        Assert.False(Assert.Single(open).Blocking);
    }

    [Theory]
    [InlineData("Diese Stelle steht nirgends im Text")]
    [InlineData(null)]
    [InlineData("kurz")]
    public void Still_there_without_a_verified_quote_is_unclear_and_never_blocks(string? quote)
    {
        var open = FactRecheck.Resolve([Finding("f1", "Die Rechnung liegt bei.")], [new RecheckResult("f1", RecheckStatus.StillThere, quote)], Revised);

        var finding = Assert.Single(open);
        Assert.False(finding.Blocking);
        Assert.Equal("Die Rechnung liegt bei.", finding.TextQuote);
        Assert.Equal(QuoteMatchKind.NotFound, finding.Match);
    }

    [Fact]
    public void Missing_answer_and_unclear_answer_are_unclear()
    {
        var open = FactRecheck.Resolve(
            [Finding("f1", "Die Rechnung liegt bei."), Finding("f2", "Der Kaufpreis wird erstattet.")],
            [new RecheckResult("f2", RecheckStatus.Unclear, null), new RecheckResult("f9", RecheckStatus.StillThere, "Fotos des Schadens liegen bei")],
            Revised);

        Assert.Equal(["f1", "f2"], open.Select(f => f.Id));
        Assert.All(open, f => Assert.False(f.Blocking));
    }

    [Fact]
    public void Quote_inside_a_placeholder_is_unclear()
    {
        const string draft = "Das Gerät wurde am [Datum der Lieferung einsetzen] geliefert.";

        var open = FactRecheck.Resolve([Finding("f1", "Das Gerät wurde gestern geliefert.")],
            [new RecheckResult("f1", RecheckStatus.StillThere, "Datum der Lieferung einsetzen")], draft);

        Assert.False(Assert.Single(open).Blocking);
    }

    [Fact]
    public void First_answer_per_id_counts()
    {
        var open = FactRecheck.Resolve([Finding("f1", "Die Rechnung liegt bei.")],
            [new RecheckResult("f1", RecheckStatus.Fixed, null), new RecheckResult("f1", RecheckStatus.StillThere, "Fotos des Schadens liegen bei")], Revised);

        Assert.Empty(open);
    }
}
