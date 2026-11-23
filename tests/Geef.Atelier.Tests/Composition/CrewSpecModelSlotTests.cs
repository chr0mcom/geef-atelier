using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Composition;

namespace Geef.Atelier.Tests.Composition;

/// <summary>D-066: composer JSON carries roles (model_slot) and settings; the composer prompt block lists slots, not models.</summary>
public sealed class CrewSpecModelSlotTests
{
    [Fact]
    public void ParserReadsModelSlotAndSettings()
    {
        var spec = CrewSpecParser.Parse("""
            {
                "mode": "new", "domain": "legal", "rationale": "r",
                "executor": {"name": "e", "model_slot": "Executor"},
                "grounding_providers": [{"name": "g", "provider_type": "tavily", "settings": {"refinementMode": "0", "MaxResults": 5, "IncludeAnswer": true, "Nothing": null}}],
                "reviewers": [{"reuse": "clarity", "model_slot": "FactReviewer"}]
            }
            """);

        Assert.Equal("Executor", spec.Executor!.ModelSlot);
        var settings = spec.GroundingProviders[0].Settings!;
        Assert.Equal("0", settings["refinementMode"]);
        Assert.Equal("5", settings["MaxResults"]);
        Assert.Equal("true", settings["IncludeAnswer"]);
        Assert.False(settings.ContainsKey("Nothing"));
        Assert.Null(spec.Reviewers[0].ModelSlot);
    }

    [Fact]
    public void ParserLeavesMissingSlotAndSettingsNull()
    {
        var spec = CrewSpecParser.Parse("""{"mode": "new", "executor": {"name": "e"}}""");

        Assert.Null(spec.Executor!.ModelSlot);
        Assert.Null(spec.Executor.Settings);
    }

    [Fact]
    public void ComposerBlockListsEverySelectableSlotAndNoLegacyModels()
    {
        var block = ComposerModelCatalog.Build(["tavily", "academic-search"]);

        foreach (var slot in new[] { ModelSlot.Executor, ModelSlot.FactReviewer, ModelSlot.StyleReviewer, ModelSlot.DomainReviewer,
                     ModelSlot.StrategicAdvisor, ModelSlot.CriticalAdvisor, ModelSlot.DomainExpertAdvisor,
                     ModelSlot.TransformRewrite, ModelSlot.TransformCondense, ModelSlot.Helper })
            Assert.Contains($"`{slot}`", block);

        Assert.DoesNotContain("`GroundingRefiner`", block);
        Assert.DoesNotContain("`Composer`", block);
        Assert.Contains("never provider/model", block);
        Assert.Contains("`academic-search`", block);
        Assert.DoesNotContain("gpt-5", block);
        Assert.DoesNotContain("grok", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolSchemaOffersModelSlotOnEveryPart()
    {
        var schema = CrewSpecTool.Schema.InputSchema.GetRawText();

        Assert.Equal(5, System.Text.RegularExpressions.Regex.Matches(schema, "\"model_slot\":\\s*\\{").Count);
        Assert.Contains("\"settings\"", schema);
    }

    [Fact]
    public void ToolSchemaRequiresModelDomainWithExactlyTheEnumNames()
    {
        var schema = CrewSpecTool.Schema.InputSchema;

        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("model_domain", required);
        var values = schema.GetProperty("properties").GetProperty("model_domain").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(Enum.GetNames<Geef.Atelier.Core.Domain.Crew.Models.ModelDomain>().Order(), values.Order());
    }

    [Fact]
    public void ParserReadsModelDomainAndKeepsInvalidValuesRaw()
    {
        var valid = CrewSpecParser.Parse("""{"mode":"new","domain":"rede-gegen-rechtsextremismus","model_domain":"Speech","rationale":"r"}""");
        var invalid = CrewSpecParser.Parse("""{"mode":"new","domain":"x","model_domain":7,"rationale":"r"}""");
        var missing = CrewSpecParser.Parse("""{"mode":"new","domain":"legal","rationale":"r"}""");

        Assert.Equal(Geef.Atelier.Core.Domain.Crew.Models.ModelDomain.Speech, valid.EffectiveModelDomain());
        Assert.Equal("7", invalid.MatrixDomain);
        Assert.Equal(Geef.Atelier.Core.Domain.Crew.Models.ModelDomain.General, invalid.EffectiveModelDomain());
        Assert.Null(missing.MatrixDomain);
        Assert.Equal(Geef.Atelier.Core.Domain.Crew.Models.ModelDomain.Legal, missing.EffectiveModelDomain());
    }
}
