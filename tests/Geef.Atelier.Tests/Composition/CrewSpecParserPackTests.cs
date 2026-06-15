using Geef.Atelier.Infrastructure.Composition;

namespace Geef.Atelier.Tests.Composition;

public sealed class CrewSpecParserPackTests
{
    [Fact]
    public void Parse_ReadsPackNamesAndNewPacks()
    {
        const string json = """
        {
            "mode": "composed",
            "executor": { "name": "e", "pack_names": ["concise-output"] },
            "reviewers": [{ "reuse": "briefing-fidelity", "pack_names": ["legal-terminology", "custom-x"] }],
            "packs": [
                {
                    "name": "custom-x",
                    "display_name": "Custom X",
                    "specialization_text": "Do X.",
                    "scope": "TaskBound",
                    "applicable_actor_types": ["Reviewer"]
                }
            ]
        }
        """;

        var spec = CrewSpecParser.Parse(json);

        Assert.Equal(new[] { "concise-output" }, spec.Executor!.PackNames);
        Assert.Equal(new[] { "legal-terminology", "custom-x" }, spec.Reviewers[0].PackNames);
        var pack = Assert.Single(spec.NewPacks);
        Assert.Equal("custom-x", pack.Name);
        Assert.Equal("Do X.", pack.SpecializationText);
        Assert.Equal("TaskBound", pack.Scope);
        Assert.Equal(new[] { "Reviewer" }, pack.ApplicableActorTypes);
    }

    [Fact]
    public void Parse_DefaultsPacksToEmpty_WhenAbsent()
    {
        const string json = """{ "mode": "composed", "executor": { "name": "e" }, "reviewers": [] }""";
        var spec = CrewSpecParser.Parse(json);
        Assert.Empty(spec.NewPacks);
        Assert.Null(spec.Executor!.PackNames);
    }
}
