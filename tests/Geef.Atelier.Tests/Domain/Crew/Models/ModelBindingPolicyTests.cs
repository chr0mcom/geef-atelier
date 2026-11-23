using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Tests.Domain.Crew.Models;

public sealed class ModelBindingPolicyTests
{
    [Fact]
    public void ManualBindingIsCliNormalizedAndLosesItsDomain()
    {
        var result = ModelBindingPolicy.ForSave(new ManagedBinding("openrouter", "anthropic/claude-opus-5", null, ModelDomain.Legal));

        Assert.Equal(new ManagedBinding("claude-cli", "claude-opus-latest", null, null), result);
    }

    [Fact]
    public void ManualCandidateModelStaysManual()
    {
        var result = ModelBindingPolicy.ForSave(new ManagedBinding("claude-cli", "claude-opus-latest", null, null));

        Assert.Null(result.Slot);
        Assert.Equal("claude-opus-latest", result.Model);
    }

    [Fact]
    public void ManagedBindingKeepsACandidate()
    {
        var incoming = new ManagedBinding("claude-cli", "claude-sonnet-latest", ModelSlot.FactReviewer, ModelDomain.Legal);

        Assert.Equal(incoming, ModelBindingPolicy.ForSave(incoming));
    }

    [Fact]
    public void ManagedBindingWithANonCandidateGetsThePrimaryOfItsDomain()
    {
        var result = ModelBindingPolicy.ForSave(new ManagedBinding("xai", "grok-4.5", ModelSlot.DomainReviewer, ModelDomain.Legal));

        Assert.Equal(new ManagedBinding("openrouter", "google/gemini-4-neon", ModelSlot.DomainReviewer, ModelDomain.Legal), result);
    }

    [Fact]
    public void ManagedBindingWithoutDomainUsesTheGeneralRow()
    {
        var result = ModelBindingPolicy.ForSave(new ManagedBinding("", "", ModelSlot.DomainReviewer, null));

        Assert.Equal(new ManagedBinding("codex-cli", "gpt-astra-latest", ModelSlot.DomainReviewer, null), result);
    }
}
