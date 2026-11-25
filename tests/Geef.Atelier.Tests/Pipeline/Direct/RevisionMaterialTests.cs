using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// What the fact check gets from a chain of changes (review E5B F1 / Sec-2): every change of the person arrives under the answers of the
/// person with a short mark that says which one is the newest and which are earlier, in their order, with the words of the person
/// unchanged; a quote from such a line, with or without its mark, is still taken as an answer of the person. Neither the prompt nor the
/// schema of the fact check changes for it.
/// </summary>
public sealed class RevisionMaterialTests
{
    private const string Request = "Schreib eine kurze Einladung zum Elternabend.";

    private const string First = "The meeting is on 5 May, not 12 May.";

    private const string Second = "The meeting is on 7 May.";

    private const string Draft = "Liebe Eltern, der Elternabend findet am 12. Mai um 19 Uhr in der Aula statt. Wir freuen uns auf Sie.";

    private static string Briefing() => RevisionBrief.Compose(RevisionBrief.Compose(Request, First, 2), Second, 3);

    [Fact]
    public void The_fact_check_reads_which_change_is_the_newest_in_the_answers_of_the_person()
    {
        var material = OrderGroundingStep.Material(Briefing(), [], new DirectOptions());

        var prompt = FactCheckPrompts.User(material, Draft);

        var answers = prompt[prompt.IndexOf("<answers_by_person>", StringComparison.Ordinal)..prompt.IndexOf("</answers_by_person>", StringComparison.Ordinal)];
        var earlier = answers.IndexOf("(earlier change, version 2; a newer change wins where they differ) " + First, StringComparison.Ordinal);
        var newest = answers.IndexOf("(newest change, version 3) " + Second, StringComparison.Ordinal);
        Assert.True(earlier >= 0, answers);
        Assert.True(newest > earlier, answers);
        Assert.Equal(Request, material.Request);
    }

    [Theory]
    [InlineData(Second)]
    [InlineData("(newest change, version 3) The meeting is on 7 May.")]
    [InlineData("The meeting is on 5 May, not 12 May")]
    public void A_quote_from_a_marked_change_still_counts_as_an_answer_of_the_person(string instruction)
    {
        var material = OrderGroundingStep.Material(Briefing(), [], new DirectOptions());
        var proposed = new ProposedFactFinding(FactFindingKind.Instruction, "der Elternabend findet am 12. Mai um 19 Uhr", "The date differs.", instruction, null, null);

        var finding = Assert.Single(FactFindingFilter.Apply([proposed], Draft, material).Findings);

        Assert.Equal(InstructionOrigin.Answered, finding.Origin);
        Assert.Equal(instruction, finding.InstructionQuote);
    }
}
