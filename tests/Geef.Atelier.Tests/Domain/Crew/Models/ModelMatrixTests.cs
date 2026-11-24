using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Domain.Crew.Models;

public sealed class ModelMatrixTests
{
    public static TheoryData<ModelSlot, ModelDomain> AllPairs()
    {
        var data = new TheoryData<ModelSlot, ModelDomain>();
        foreach (var slot in Enum.GetValues<ModelSlot>())
            foreach (var domain in Enum.GetValues<ModelDomain>())
                data.Add(slot, domain);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void EveryPairHasDistinctCandidates(ModelSlot slot, ModelDomain domain)
    {
        var candidates = ModelMatrix.CandidatesFor(slot, domain);

        Assert.NotEmpty(candidates);
        Assert.Equal(candidates.Count, candidates.Distinct().Count());
        Assert.Equal(candidates[0], ModelMatrix.Primary(slot, domain));
    }

    [Fact]
    public void ClaudeAndOpenAiCandidatesAlwaysUseTheSubscriptionClis()
    {
        foreach (var candidate in ModelMatrix.All)
        {
            switch (candidate.Family())
            {
                case VendorFamily.Anthropic:
                    Assert.Equal(ModelMatrix.ClaudeCli, candidate.Provider);
                    break;
                case VendorFamily.OpenAi:
                    Assert.Equal(ModelMatrix.CodexCli, candidate.Provider);
                    break;
            }

            Assert.False(CliRoute.IsNonCliRoute(candidate.Provider, candidate.Model), candidate.ToString());
        }
    }

    [Fact]
    public void NoRetiredOrLowCapabilityModelIsACandidate()
    {
        string[] forbidden = ["gpt-5", "gpt-4", "luna", "grok", "deepseek", "gemini-3", "gemini-2", "-mini", "terra"];

        foreach (var candidate in ModelMatrix.All)
            Assert.DoesNotContain(forbidden, f => candidate.Model.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReviewerListsEndWithASubscriptionClaudeFallback()
    {
        foreach (var slot in new[] { ModelSlot.FactReviewer, ModelSlot.StyleReviewer, ModelSlot.DomainReviewer })
            foreach (var domain in Enum.GetValues<ModelDomain>())
                Assert.Equal(new ModelCandidate("claude-cli", "claude-sonnet-latest"), ModelMatrix.CandidatesFor(slot, domain)[^1]);
    }

    [Theory]
    [InlineData(ModelSlot.Executor, ModelDomain.Correspondence, "claude-cli", "claude-opus-latest")]
    [InlineData(ModelSlot.Executor, ModelDomain.Legal, "claude-cli", "claude-opus-latest")]
    [InlineData(ModelSlot.Executor, ModelDomain.Technical, "claude-cli", "claude-fable-latest")]
    [InlineData(ModelSlot.FactReviewer, ModelDomain.General, "codex-cli", "gpt-astra-latest")]
    [InlineData(ModelSlot.StyleReviewer, ModelDomain.Speech, "codex-cli", "gpt-sol-latest")]
    [InlineData(ModelSlot.DomainReviewer, ModelDomain.Legal, "openrouter", "google/gemini-4-neon")]
    [InlineData(ModelSlot.DomainReviewer, ModelDomain.Academic, "openrouter", "google/gemini-4-neon")]
    [InlineData(ModelSlot.DomainReviewer, ModelDomain.Marketing, "codex-cli", "gpt-astra-latest")]
    [InlineData(ModelSlot.StrategicAdvisor, ModelDomain.General, "claude-cli", "claude-fable-latest")]
    [InlineData(ModelSlot.CriticalAdvisor, ModelDomain.General, "claude-cli", "claude-fable-latest")]
    [InlineData(ModelSlot.DomainExpertAdvisor, ModelDomain.Academic, "codex-cli", "gpt-astra-latest")]
    [InlineData(ModelSlot.DomainExpertAdvisor, ModelDomain.Legal, "claude-cli", "claude-opus-latest")]
    [InlineData(ModelSlot.GroundingRefiner, ModelDomain.General, "claude-cli", "claude-sonnet-latest")]
    [InlineData(ModelSlot.TransformRewrite, ModelDomain.General, "codex-cli", "gpt-sol-latest")]
    [InlineData(ModelSlot.TransformCondense, ModelDomain.General, "claude-cli", "claude-sonnet-latest")]
    [InlineData(ModelSlot.Composer, ModelDomain.General, "claude-cli", "claude-opus-latest")]
    [InlineData(ModelSlot.Helper, ModelDomain.General, "claude-cli", "claude-haiku-latest")]
    [InlineData(ModelSlot.Intake, ModelDomain.General, "claude-cli", "claude-sonnet-latest")]
    public void PrimariesMatchTheResearchedTable(ModelSlot slot, ModelDomain domain, string provider, string model) =>
        Assert.Equal(new ModelCandidate(provider, model), ModelMatrix.Primary(slot, domain));

    [Fact]
    public void PaidCandidatesAreExactlyTheNonCliOnes()
    {
        Assert.True(new ModelCandidate("openrouter", "google/gemini-4-neon").IsPaid);
        Assert.False(new ModelCandidate("claude-cli", "claude-opus-latest").IsPaid);
        Assert.False(new ModelCandidate("codex-cli", "gpt-sol-latest").IsPaid);
    }

    [Fact]
    public void CodexIdsAreConsistentAcrossMatrixFallbacksAndProviderSeeds()
    {
        var matrixCodex = ModelMatrix.All.Where(c => c.Provider == ModelMatrix.CodexCli).Select(c => c.Model).ToHashSet();
        var fallback = StaticModelFallback.ForCodexCli.Select(m => m.Id).ToHashSet();
        var seeded = CliProviderSettings.FromSettings(SystemProviders.CodexCli.Settings).Models.ToHashSet();

        Assert.Subset(ModelMatrix.CodexSubscriptionModels.ToHashSet(), matrixCodex);
        Assert.Contains(ModelMatrix.RetiredOpenAiReplacement, ModelMatrix.CodexSubscriptionModels);
        Assert.Subset(fallback, matrixCodex);
        Assert.Subset(seeded, matrixCodex);
        Assert.Equal(ModelMatrix.CodexSubscriptionModels.ToHashSet(), fallback);
    }

    [Fact]
    public void OpenRouterFallbackCarriesNoClaudeOrOpenAiModels()
    {
        Assert.DoesNotContain(StaticModelFallback.ForOpenRouter, m => m.Id.StartsWith("anthropic/") || m.Id.StartsWith("openai/"));
        Assert.Contains(StaticModelFallback.ForOpenRouter, m => m.Id == "google/gemini-4-neon");
    }

    [Fact]
    public void RetiredCodexReplacementsAreSubscriptionModels()
    {
        foreach (var retired in new[] { "gpt-5.6-sol", "gpt-5.6-terra", "gpt-4o", "o3", "gpt-6-luna" })
        {
            var replacement = CliRoute.Normalize(ModelMatrix.CodexCli, retired);
            Assert.Contains(replacement.Model, ModelMatrix.CodexSubscriptionModels);
            Assert.Contains(replacement.Model, StaticModelFallback.ForCodexCli.Select(m => m.Id));
        }
    }

    [Fact]
    public void CliProxyCodexFallbackMatchesTheAtelierLists()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "cli-proxy", "src", "codex_adapter.py"));
        var block = System.Text.RegularExpressions.Regex.Match(source, @"_FALLBACK_MODELS\s*=\s*\[(?<ids>[^\]]*)\]");
        Assert.True(block.Success, "_FALLBACK_MODELS not found in codex_adapter.py");
        var proxyFallback = System.Text.RegularExpressions.Regex.Matches(block.Groups["ids"].Value, "\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(proxyFallback);
        foreach (var id in proxyFallback)
        {
            Assert.Contains(id, ModelMatrix.CodexSubscriptionModels);
            Assert.Contains(id, StaticModelFallback.ForCodexCli.Select(m => m.Id));
        }

        Assert.Contains(proxyFallback, id => id.EndsWith("-sol", StringComparison.Ordinal));
        Assert.Contains(proxyFallback, id => id.EndsWith("-astra", StringComparison.Ordinal));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Geef.Atelier.slnx) not found.");
    }
}
