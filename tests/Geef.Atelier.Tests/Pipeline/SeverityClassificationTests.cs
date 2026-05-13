using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk.Context;
using Geef.Sdk.Results;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class SeverityClassificationTests
{
    private static ReviewerProfile TestProfile(string systemPrompt) => new(
        Name: "test-reviewer",
        DisplayName: "Test",
        Description: "Test reviewer",
        SystemPrompt: systemPrompt,
        Provider: "test",
        Model: "test-model",
        MaxTokens: null,
        IsSystem: false);

    [Theory]
    [InlineData("critical", FindingSeverity.Critical)]
    [InlineData("major",    FindingSeverity.Error)]
    [InlineData("minor",    FindingSeverity.Warning)]
    [InlineData("info",     FindingSeverity.Info)]
    [InlineData("error",    FindingSeverity.Error)]    // backwards-compat
    [InlineData("warning",  FindingSeverity.Warning)]  // backwards-compat
    public async Task ProfileBasedReviewer_MapsSeverityCorrectly(string severityString, FindingSeverity expectedSdkSeverity)
    {
        var client   = new FixedSeverityLlmClient(severityString);
        var resolver = new TestLlmClientResolver(client);
        var reviewer = new ProfileBasedReviewer(TestProfile("You are a reviewer."), resolver);
        var context  = BuildContext("Some briefing.", "Some draft.");

        var result = await reviewer.ReviewAsync(context, CancellationToken.None);

        Assert.Single(result.Findings);
        Assert.Equal(expectedSdkSeverity, result.Findings[0].Severity);
    }

    [Fact]
    public async Task ProfileBasedReviewer_UnknownSeverity_DefaultsToWarning()
    {
        var client   = new FixedSeverityLlmClient("unknown-value");
        var resolver = new TestLlmClientResolver(client);
        var reviewer = new ProfileBasedReviewer(TestProfile("You are a reviewer."), resolver);
        var context  = BuildContext("Some briefing.", "Some draft.");

        var result = await reviewer.ReviewAsync(context, CancellationToken.None);

        Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Warning, result.Findings[0].Severity);
    }

    private static IRunContext BuildContext(string briefing, string draft) =>
        new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, briefing)
            .Set(AtelierContextKeys.CurrentDraft,  draft);
}

internal sealed class FixedSeverityLlmClient(string severity) : ILlmClient
{
    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var json = $$$"""{"approved":false,"findings":[{"severity":"{{{severity}}}","message":"test finding"}]}""";
        return Task.FromResult(new LlmResponse
        {
            Text              = "",
            FinishReason      = "tool_calls",
            ToolName          = "submit_review",
            ToolArgumentsJson = json,
            TokenUsage        = new LlmTokenUsage { InputTokens = 1, OutputTokens = 1 }
        });
    }
}
