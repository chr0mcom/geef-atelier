using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

namespace Geef.Atelier.Tests.Pipeline.Direct.FactCheck;

/// <summary>
/// The research of stage 4 reaches the fact check only as one more source of the material. Prompts, schemas and version of the check stay
/// those of the measured state (V2), so the stored measurement stays valid; the expected hashes were taken from the sources of that state.
/// </summary>
public sealed class FactCheckPromptStabilityTests
{
    private const string SystemHash = "0E7E1D39AE5D4A86D6B59D675E90A9E9D7BFE4787A6B45FDF97C10C282D91862";
    private const string RecheckSystemHash = "3AF34EC77952C398A08BB79FB1161EB1CA93B9546969B18EAA9DDEB692036AF0";
    private const string CheckSchemaHash = "43AC9BEF524A7D7867775A5C39428BE285AC386EC16D89EBA64FD7AF910BFD33";
    private const string RecheckSchemaHash = "25C612CFD3F43A434C0BFFF971BDAEED3F9CBEFD9595A1F25C29F3CEDCF90EF4";

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));

    [Fact]
    public void System_prompts_schemas_and_version_are_those_of_the_measured_state()
    {
        Assert.Equal(SystemHash, Hash(FactCheckPrompts.System));
        Assert.Equal(RecheckSystemHash, Hash(FactCheckPrompts.RecheckSystem));
        Assert.Equal(CheckSchemaHash, Hash(FactCheckSchema.CheckJson));
        Assert.Equal(RecheckSchemaHash, Hash(FactCheckSchema.RecheckJson));
        Assert.Equal(("check_facts", "recheck_facts"), (FactCheckSchema.CheckName, FactCheckSchema.RecheckName));
        Assert.Equal("fact-check-1", FactCheckPrompts.Version);
    }

    [Fact]
    public void The_looked_up_passages_are_one_more_source_of_the_material_and_nothing_else_changes()
    {
        var file = new FactSource("S1", "notizen.txt", "Der Elternabend ist am 3. März.");
        var lookedUp = new FactSource(DossierRenderer.SourceId, DossierRenderer.FileName, "[W1.1] Der Mindestlohn beträgt seit Januar 12,82 Euro.", LookedUp: true);
        var without = new FactMaterial("Schreib einen Aushang.", [], [], null, [file], FactCheckScope.Full, null);
        var with = without with { Files = [file, lookedUp] };

        var before = FactCheckPrompts.User(without, "Ein Text.");
        var after = FactCheckPrompts.User(with, "Ein Text.");

        var source = $"<source id=\"W1\" name=\"Looked up on the web\">\n{lookedUp.Text}\n</source>\n";
        Assert.Contains(source, after, StringComparison.Ordinal);
        Assert.Equal(before, after.Replace(source, string.Empty, StringComparison.Ordinal));
    }
}
