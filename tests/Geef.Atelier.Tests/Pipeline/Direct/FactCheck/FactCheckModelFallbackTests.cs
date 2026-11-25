using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

namespace Geef.Atelier.Tests.Pipeline.Direct.FactCheck;

/// <summary>The one fallback of the fact check: next candidate of the slot, never the writer's model, never the provider that failed, never paid.</summary>
public sealed class FactCheckModelFallbackTests
{
    private static readonly ReviewerProfile Codex = SystemCrew.SourceFactCheckProfile;

    [Theory]
    [InlineData("claude-opus-latest")]
    [InlineData("claude-opus-5-5")]
    public void Codex_failing_next_to_an_opus_writer_falls_back_to_sonnet(string writerModel)
    {
        var fallback = FactCheckModelFallback.For(Codex, ModelMatrix.ClaudeCli, writerModel);

        Assert.NotNull(fallback);
        Assert.Equal((ModelMatrix.ClaudeCli, "claude-sonnet-latest"), (fallback.Provider, fallback.Model));
        Assert.Equal(Codex.Name, fallback.Name);
    }

    [Fact]
    public void A_sonnet_writer_gets_opus_as_fallback()
    {
        var fallback = FactCheckModelFallback.For(Codex, ModelMatrix.ClaudeCli, "claude-sonnet-latest");

        Assert.Equal("claude-opus-latest", fallback!.Model);
    }

    [Fact]
    public void A_failing_claude_check_never_falls_back_to_the_same_provider()
    {
        var failed = Codex with { Provider = ModelMatrix.ClaudeCli, Model = "claude-sonnet-latest" };

        var fallback = FactCheckModelFallback.For(failed, ModelMatrix.ClaudeCli, "claude-opus-latest");

        Assert.Equal(ModelMatrix.CodexCli, fallback!.Provider);
    }

    [Fact]
    public void Without_a_permitted_candidate_there_is_no_fallback()
    {
        var failed = Codex with { Provider = ModelMatrix.ClaudeCli, Model = "claude-sonnet-latest" };

        Assert.Null(FactCheckModelFallback.For(failed, ModelMatrix.CodexCli, ModelMatrix.CodexAstraAlias));
    }

    [Theory]
    [InlineData("claude-opus-latest")]
    [InlineData("claude-sonnet-latest")]
    [InlineData("claude-fable-latest")]
    [InlineData(ModelMatrix.CodexSolAlias)]
    public void The_fallback_is_never_paid_and_never_the_writer(string writerModel)
    {
        var fallback = FactCheckModelFallback.For(Codex, ModelMatrix.ClaudeCli, writerModel);

        if (fallback is null)
            return;
        Assert.False(new ModelCandidate(fallback.Provider, fallback.Model).IsPaid);
        Assert.False(CliRoute.IsSameModel(fallback.Model, writerModel));
        Assert.NotEqual(Codex.Provider, fallback.Provider);
    }
}
