using System.Text.Json.Nodes;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.EndToEnd.Fakes;

public sealed class EndToEndFakesTests
{
    [Fact]
    public void Document_mode_request_is_classified_as_executor_and_exposes_context_document()
    {
        var body = new JsonObject
        {
            ["model"] = "claude-opus-latest",
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = SystemPrompts.DirectWriter },
                new JsonObject { ["role"] = "user", ["content"] = "Briefing:\nx" }),
            ["document_mode"] = true,
            ["document"] = "",
            ["context_document"] = "MARKER-CONTEXT",
        }.ToJsonString();

        var call = ScriptedLlmEndpoint.Parse(1, "POST", "http://cli-proxy/v1/claude/chat/completions", body);

        Assert.Equal(LlmRole.Executor, call.Role);
        Assert.Contains("MARKER-CONTEXT", call.PromptText);
        Assert.Equal("claude-opus-latest", call.Model);
    }

    [Fact]
    public void Request_with_submit_review_tool_is_classified_as_reviewer()
    {
        var body = new JsonObject
        {
            ["model"] = "gpt-sol-latest",
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Draft" }),
            ["tools"] = new JsonArray(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "submit_review", ["parameters"] = new JsonObject() },
            }),
            ["tool_choice"] = new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "submit_review" },
            },
        }.ToJsonString();

        var call = ScriptedLlmEndpoint.Parse(2, "POST", "http://cli-proxy/v1/codex/chat/completions", body);

        Assert.Equal(LlmRole.Reviewer, call.Role);
        Assert.Equal("function:submit_review", call.ToolChoice);
    }

    [Fact]
    public void Fake_embeddings_rank_similar_text_above_unrelated_text()
    {
        var query = FakeEmbeddingsEndpoint.Embed("history of the Nordkap lighthouse", 1536);
        var similar = FakeEmbeddingsEndpoint.Embed("The Nordkap lighthouse history notes", 1536);
        var unrelated = FakeEmbeddingsEndpoint.Embed("quarterly invoice for office chairs", 1536);

        Assert.True(Cosine(query, similar) > Cosine(query, unrelated));
        Assert.Equal(1536, query.Length);
    }

    private static double Cosine(float[] a, float[] b) =>
        a.Zip(b, (x, y) => (double)x * y).Sum();
}
