using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Infrastructure.Intake;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// Changing a text without the button “Longer” keeps every sentence the measurements of changing (Tor N′) and of the checks (V2, D-072)
/// were made with: the revision sentence for the writer, the writer's system prompt, the clarifier and the fact check prompts and schemas,
/// and the fixed sentences of the quick buttons. The expected hashes were taken from the sources before the button was removed.
/// </summary>
public sealed class RevisionPromptPinTests
{
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));

    [Fact]
    public void The_revision_sentence_for_the_writer_is_the_measured_one()
    {
        Assert.Equal("F5872BC30D607D0B6FA8CAEC3B0DCA1BD051D69269EE388109872F3AC59A6E22", Hash(DirectWriterPrompts.RevisionInstruction));
    }

    [Fact]
    public void Writer_clarifier_and_fact_check_prompts_are_the_measured_ones()
    {
        Assert.Equal("811508C1F3A49FC07734F9372591AC0AB7AD311B447DE0A2AF0042B468C1D363", Hash(SystemPrompts.DirectWriter));
        Assert.Equal("E17689575084495E9EB229B14F071FA4356F5199D01C2E11CCBF0C3BD48F6A42", Hash(ClarifierPrompts.System));
        Assert.Equal("53EB627AF5AD8EFA2E14B6334140A0AEFDDA77F4A30AB83819C49FEFB10A216D", Hash(ClarifierSchema.Json));
        Assert.Equal("0E7E1D39AE5D4A86D6B59D675E90A9E9D7BFE4787A6B45FDF97C10C282D91862", Hash(FactCheckPrompts.System));
        Assert.Equal("43AC9BEF524A7D7867775A5C39428BE285AC386EC16D89EBA64FD7AF910BFD33", Hash(FactCheckSchema.CheckJson));
    }

    [Theory]
    [InlineData(RevisionQuick.Shorter, "66BC7D763A85DB551EE49857200B8783C04B352FC2B5D0AA7A8ACA589FF000B2")]
    [InlineData(RevisionQuick.Longer, "77DCB9D8FFF39B03443B89C791ACE08BA7B173C75A0293DC3DD0617E75501A07")]
    [InlineData(RevisionQuick.MoreFormal, "20AC8168BF354AF6F9D6B7A9F1787E4B5931209BE26C0C3846288C45E2E7B0BC")]
    [InlineData(RevisionQuick.Friendlier, "20BAF8007F46CA879437C3FFBA989C940EBD2C523256E802E796C55303BC9EA8")]
    [InlineData(RevisionQuick.Simpler, "CB4CDF0D8F3447BA153CE95C6B3499F863AD64FB95B268C31A53DB064DB8588D")]
    public void The_fixed_sentences_of_the_quick_buttons_are_unchanged(RevisionQuick quick, string expected)
    {
        Assert.Equal(expected, Hash(quick.Instruction()));
    }
}
