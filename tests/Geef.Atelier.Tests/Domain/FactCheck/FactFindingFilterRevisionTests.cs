using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Domain.FactCheck;

/// <summary>
/// The fact check in a run that changes an earlier version (review E5B round 2, R1 and minor 1): a finding against an instruction blocks
/// only when its instruction stands in the newest change; one from the request, the answers, the About me details or an earlier change
/// stays visible as a hint marked <see cref="FactFinding.BeforeLastChange"/>. The marks of the changes never count as words of the person
/// and never stand in a kept quote. Findings of other kinds and the first run of an order stay as they were.
/// </summary>
public sealed class FactFindingFilterRevisionTests
{
    private const string Request = "Write a short invitation to the parents' evening. Do not mention the price.";

    private const string Answer = "Please write in a formal tone";

    private const string ClientAnswer = "End the letter with greetings from the class teacher";

    private const string Profile = "Name: Alex Example. Never use exclamation marks.";

    private const string Earlier = "Say that the evening is in the hall.";

    private const string Newest = "Please add that the evening costs 5 euros per family.";

    private const string FileText = "Notes: the evening is on Thursday at 7 pm in the hall. The old date on Wednesday is cancelled.";

    private const string Draft = """
        Dear parents,

        the parents' evening takes place on Thursday at 7 pm in the hall. The evening costs 5 euros per family.
        We look forward to seeing you all there!

        Kind regards
        Alex Example
        """;

    private const string PricePassage = "The evening costs 5 euros per family.";

    private static FactMaterial Material(bool revision = true, string newest = Newest) =>
        new(Request, [Answer], [ClientAnswer], Profile, [new FactSource("S1", "notes.txt", FileText)], FactCheckScope.Full, null,
            revision ? [new FactChange(2, Earlier, Newest: false), new FactChange(3, newest, Newest: true)] : null);

    private static ProposedFactFinding Instruction(string instruction, string quote = PricePassage) =>
        new(FactFindingKind.Instruction, quote, "Goes against the instruction.", instruction, null, null);

    private static FactFinding Kept(ProposedFactFinding proposed, FactMaterial? material = null)
    {
        var result = FactFindingFilter.Apply([proposed], Draft, material ?? Material());
        Assert.Equal(FactFilterDecision.Kept, Assert.Single(result.Trace).Decision);
        return Assert.Single(result.Findings);
    }

    [Theory]
    [InlineData("Do not mention the price.", InstructionOrigin.Stated)]
    [InlineData(Answer, InstructionOrigin.Answered)]
    [InlineData(Earlier, InstructionOrigin.Answered)]
    [InlineData("Never use exclamation marks.", InstructionOrigin.Memory)]
    [InlineData(ClientAnswer, InstructionOrigin.Client)]
    public void An_instruction_from_before_the_newest_change_is_kept_as_a_hint_that_never_blocks(string instruction, InstructionOrigin origin)
    {
        var finding = Kept(Instruction(instruction));

        Assert.False(finding.Blocking);
        Assert.True(finding.BeforeLastChange);
        Assert.Equal(origin, finding.Origin);
        Assert.Equal(instruction, finding.InstructionQuote);
    }

    [Fact]
    public void An_instruction_from_the_newest_change_blocks()
    {
        var finding = Kept(Instruction("costs 5 euros per family", "the parents' evening takes place on Thursday"));

        Assert.True(finding.Blocking);
        Assert.False(finding.BeforeLastChange);
        Assert.Equal(InstructionOrigin.Answered, finding.Origin);
    }

    [Fact]
    public void An_instruction_that_stands_in_the_newest_change_and_earlier_counts_as_the_newest()
    {
        var material = Material(newest: "Do not mention the price. Say that it starts at 7 pm.");

        var finding = Kept(Instruction("Do not mention the price."), material);

        Assert.True(finding.Blocking);
        Assert.False(finding.BeforeLastChange);
    }

    [Fact]
    public void In_the_first_run_of_an_order_an_instruction_of_the_request_still_blocks()
    {
        var finding = Kept(Instruction("Do not mention the price."), Material(revision: false));

        Assert.True(finding.Blocking);
        Assert.False(finding.BeforeLastChange);
        Assert.Equal(InstructionOrigin.Stated, finding.Origin);
    }

    private const string TariffRequest =
        "Write about our offer. a) Tarif Version 1) kostet 10 Euro pro Monat b) Tarif Version 2) kostet 12 Euro pro Monat. Halte dich an Entwurf Version 2) und schreibe kurz.";

    private const string TariffQuote = "Tarif Version 2) kostet 12 Euro pro Monat";

    private static FactMaterial TariffMaterial(bool revision) =>
        new(TariffRequest, [], [], null, [], FactCheckScope.Full, null,
            revision ? [new FactChange(2, "Make it shorter.", Newest: true)] : null);

    [Fact]
    public void In_the_first_run_of_an_order_a_quote_that_starts_like_the_end_of_a_mark_keeps_its_words()
    {
        var instruction = Kept(Instruction(TariffQuote, "the parents' evening takes place on Thursday"), TariffMaterial(revision: false));
        var superseded = Kept(new ProposedFactFinding(FactFindingKind.Superseded, "We look forward to seeing you all there!", "Replaced.", null,
            TariffQuote, null), TariffMaterial(revision: false));

        Assert.Equal(TariffQuote, instruction.InstructionQuote);
        Assert.Equal(InstructionOrigin.Stated, instruction.Origin);
        Assert.True(instruction.Blocking);
        Assert.False(instruction.BeforeLastChange);
        Assert.Equal(TariffQuote, superseded.MaterialQuote);
        Assert.Equal(MaterialPart.Request, superseded.MaterialFrom);
    }

    [Fact]
    public void In_the_first_run_of_an_order_an_instruction_quote_like_the_end_of_a_mark_still_blocks()
    {
        var proposed = Instruction("Entwurf Version 2)", "the parents' evening takes place on Thursday");

        var first = Kept(proposed, TariffMaterial(revision: false));
        var noChanges = Kept(proposed, TariffMaterial(revision: false) with { Changes = [] });

        Assert.Equal("Entwurf Version 2)", first.InstructionQuote);
        Assert.Equal(InstructionOrigin.Stated, first.Origin);
        Assert.True(first.Blocking);
        Assert.Equal(first, noChanges);
    }

    [Fact]
    public void Only_a_run_that_changes_an_earlier_version_takes_the_end_of_a_mark_out_of_a_quote()
    {
        var revision = FactFindingFilter.Apply([Instruction("Entwurf Version 2)", "the parents' evening takes place on Thursday")], Draft,
            TariffMaterial(revision: true));
        var tariff = Kept(Instruction(TariffQuote, "the parents' evening takes place on Thursday"), TariffMaterial(revision: true));

        Assert.Equal(FactFilterDecision.DroppedInstructionNotFound, Assert.Single(revision.Trace).Decision);
        Assert.Equal("kostet 12 Euro pro Monat", tariff.InstructionQuote);
        Assert.True(tariff.BeforeLastChange);
    }

    [Fact]
    public void Other_kinds_of_findings_block_in_a_change_as_before()
    {
        var material = Material();
        var result = FactFindingFilter.Apply(
            [
                new ProposedFactFinding(FactFindingKind.Unsupported, "We look forward to seeing you all there!", "Not in the material.", null, null, null),
                new ProposedFactFinding(FactFindingKind.Superseded, "the parents' evening takes place on Thursday", "Uses the old date.", null, "The old date on Wednesday is cancelled.", null),
            ], Draft, material);

        Assert.All(result.Findings, f => Assert.True(f.Blocking));
        Assert.All(result.Findings, f => Assert.False(f.BeforeLastChange));
        Assert.Equal(2, result.Findings.Count);
    }

    [Theory]
    [InlineData("newest change, version 3")]
    [InlineData("(newest change, version 3)")]
    [InlineData("earlier change, version 2; a newer change wins where they differ")]
    [InlineData("a newer change wins where they differ")]
    public void A_quote_that_is_only_a_mark_is_never_taken_as_an_instruction_of_the_person(string mark)
    {
        var result = FactFindingFilter.Apply([Instruction(mark)], Draft, Material());

        Assert.Empty(result.Findings);
        Assert.Equal(FactFilterDecision.DroppedInstructionNotFound, Assert.Single(result.Trace).Decision);
    }

    [Theory]
    [InlineData("(newest change, version 3) " + Newest, Newest, true)]
    [InlineData("version 3) " + Newest, Newest, true)]
    [InlineData("(earlier change, version 2; a newer change wins where they differ) " + Earlier, Earlier, false)]
    public void A_quote_with_a_mark_is_kept_without_the_mark(string quoted, string kept, bool blocking)
    {
        var finding = Kept(Instruction(quoted, "the parents' evening takes place on Thursday"));

        Assert.Equal(kept, finding.InstructionQuote);
        Assert.Equal(blocking, finding.Blocking);
        Assert.DoesNotContain("change, version", finding.InstructionQuote, StringComparison.Ordinal);
    }

    [Fact]
    public void A_material_quote_of_a_change_is_kept_without_the_mark()
    {
        var conflict = new ProposedFactFinding(FactFindingKind.SourceConflict, null, "The places differ.", null,
            "(earlier change, version 2; a newer change wins where they differ) " + Earlier, "the evening is on Thursday at 7 pm in the hall");
        var superseded = new ProposedFactFinding(FactFindingKind.Superseded, "We look forward to seeing you all there!", "Replaced.", null,
            "(newest change, version 3) " + Newest, null);

        var result = FactFindingFilter.Apply([conflict, superseded], Draft, Material());

        Assert.Equal(2, result.Findings.Count);
        Assert.Equal(Earlier, result.Findings[0].MaterialQuote);
        Assert.Equal(MaterialPart.PersonAnswers, result.Findings[0].MaterialFrom);
        Assert.Equal(Newest, result.Findings[1].MaterialQuote);
        Assert.All(result.Findings, f => Assert.DoesNotContain("change, version", (f.MaterialQuote ?? string.Empty) + f.MaterialQuote2, StringComparison.Ordinal));
    }

    [Fact]
    public void The_marks_are_written_out_only_for_the_check_and_never_looked_in()
    {
        var material = Material();

        Assert.Equal("(newest change, version 3) " + Newest, material.Changes![1].Marked);
        Assert.Equal("(earlier change, version 2; a newer change wins where they differ) " + Earlier, material.Changes[0].Marked);
        Assert.Equal(Newest, FactChange.WithoutMarks("(newest change, version 3) " + Newest));
        Assert.Equal("Keep (this) and version 2 of the logo.", FactChange.WithoutMarks("Keep (this) and version 2 of the logo."));
        Assert.Equal(string.Empty, FactChange.WithoutMarks("(earlier change, version ?; a newer change wins where they differ)"));
    }

    [Fact]
    public void A_finding_carried_over_from_an_earlier_version_blocks_only_when_its_instruction_stands_in_the_newest_change()
    {
        FactFinding Carried(string instruction) => new("f1", FactFindingKind.Instruction, PricePassage, "x", instruction, null, null, null,
            InstructionOrigin.Stated, true, QuoteMatchKind.Exact, FromVersion: 1);

        var fromRequest = FactFindingFilter.ForRevision(Carried("Do not mention the price."), Material());
        var fromNewest = FactFindingFilter.ForRevision(Carried("costs 5 euros per family"), Material());
        var unknownMaterial = FactFindingFilter.ForRevision(Carried("costs 5 euros per family"), null);
        var unsupported = Carried("x") with { Kind = FactFindingKind.Unsupported, InstructionQuote = null };

        Assert.Equal((false, true), (fromRequest.Blocking, fromRequest.BeforeLastChange));
        Assert.Equal((true, false), (fromNewest.Blocking, fromNewest.BeforeLastChange));
        Assert.Equal((false, true), (unknownMaterial.Blocking, unknownMaterial.BeforeLastChange));
        Assert.Same(unsupported, FactFindingFilter.ForRevision(unsupported, Material()));
    }

    [Fact]
    public void A_hint_from_before_the_newest_change_is_never_made_blocking_again_by_a_repeated_finding()
    {
        var hint = Kept(Instruction("Do not mention the price."));
        var earlierBlocking = hint with { Blocking = true, BeforeLastChange = false, FromVersion = 1 };

        var merged = FactCarryOver.Merge([hint], [[earlierBlocking]], Draft);

        var finding = Assert.Single(merged);
        Assert.False(finding.Blocking);
        Assert.True(finding.BeforeLastChange);
    }
}
