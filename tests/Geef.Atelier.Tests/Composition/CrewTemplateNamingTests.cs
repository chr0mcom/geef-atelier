using Geef.Atelier.Infrastructure.Composition;

namespace Geef.Atelier.Tests.Composition;

/// <summary>
/// Guards the auto-composed crew-template name generation. The decisive constraint is that the
/// final name (custom- prefix + slug + -auto-&lt;timestamp&gt; + optional dedup suffix) must fit the
/// <c>Runs.CrewTemplateName</c> varchar(100) column; otherwise the chained task run fails to insert
/// and the composition silently stops chaining (regression from run d5287ee7).
/// </summary>
public sealed class CrewTemplateNamingTests
{
    [Fact]
    public void BuildAutoTemplateName_KeepsFinalNameWithin100Chars_ForLongDomain()
    {
        // The exact domain that produced the 160-char name and the varchar(100) overflow.
        const string domain =
            "Sozialwissenschaft / experimentelle Verhaltensforschung " +
            "(verhaltensökonomisches Studiendesign für Multi-Agent-LLM-Kooperationsexperimente)";

        var name = CrewTemplateNaming.BuildAutoTemplateName(domain);

        // Worst case the CrewService adds "custom-" (7) and a "-99" dedup suffix (3).
        Assert.True(
            ("custom-" + name + "-99").Length <= 100,
            $"Final template name too long: 'custom-{name}-99' = {("custom-" + name + "-99").Length} chars");
    }

    [Fact]
    public void BuildAutoTemplateName_ProducesCleanKebabSlug()
    {
        var name = CrewTemplateNaming.BuildAutoTemplateName("Legal / Contract (Risk) Review");

        // No structural punctuation, no whitespace, no leading/trailing or doubled hyphens.
        Assert.DoesNotContain('/', name);
        Assert.DoesNotContain('(', name);
        Assert.DoesNotContain(')', name);
        Assert.DoesNotContain(' ', name);
        Assert.DoesNotContain("--", name);
        Assert.False(name.StartsWith('-'));
        Assert.StartsWith("legal-contract-risk-review-auto-", name);
    }

    [Fact]
    public void BuildAutoTemplateName_PreservesUmlauts()
    {
        // Umlauts are valid UTF-8 and must not be ASCII-substituted (ue/oe/ae).
        var name = CrewTemplateNaming.BuildAutoTemplateName("Akademische Prüfung");

        Assert.Contains("prüfung", name);
        Assert.DoesNotContain("pruefung", name);
    }

    [Fact]
    public void BuildAutoTemplateName_FallsBackForEmptyDomain()
    {
        var name = CrewTemplateNaming.BuildAutoTemplateName("   ");

        Assert.StartsWith("crew-auto-", name);
    }
}
