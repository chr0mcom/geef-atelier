using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Infrastructure.Llm;

public sealed class ReasoningEffortFormatTests
{
    private static LlmRequest Request(string? effort) => new()
    {
        Model = "test-model",
        SystemPrompt = "sys",
        UserPrompt = "user",
        ReasoningEffort = effort,
    };

    [Fact]
    public void ReasoningEffortIsSentWhenSet()
    {
        using var doc = JsonDocument.Parse(OpenAiMessageFormat.SerializeRequest(Request(" low ")));

        Assert.Equal("low", doc.RootElement.GetProperty("reasoning_effort").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReasoningEffortIsLeftOutWithoutValue(string? effort)
    {
        var json = OpenAiMessageFormat.SerializeRequest(Request(effort));

        Assert.DoesNotContain("reasoning_effort", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestWithoutEffortIsUnchanged()
    {
        var withoutField = OpenAiMessageFormat.SerializeRequest(Request(null));

        Assert.Equal("""{"model":"test-model","messages":[{"role":"system","content":"sys"},{"role":"user","content":"user"}],"max_tokens":16384}""", withoutField);
    }
}
