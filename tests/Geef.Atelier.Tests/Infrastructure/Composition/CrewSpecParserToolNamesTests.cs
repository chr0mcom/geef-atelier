using Geef.Atelier.Infrastructure.Composition;

namespace Geef.Atelier.Tests.Infrastructure.Composition;

/// <summary>
/// Unit tests verifying that <see cref="CrewSpecParser"/> correctly parses the
/// <c>tool_names</c> field on each actor type into <c>CrewPartSpec.ToolNames</c>.
/// </summary>
public sealed class CrewSpecParserToolNamesTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MakeExecutorJson(string toolNamesFragment) => $$"""
        {
            "mode": "new",
            "domain": "general",
            "rationale": "test",
            "executor": {
                "name": "test-executor",
                "display_name": "Test Executor",
                "system_prompt": "Write.",
                "provider": "claude-cli",
                "model": "claude-opus-4-8"
                {{toolNamesFragment}}
            }
        }
        """;

    private static string MakeReviewerJson(string toolNamesFragment) => $$"""
        {
            "mode": "new",
            "domain": "general",
            "rationale": "test",
            "reviewers": [
                {
                    "name": "test-reviewer",
                    "display_name": "Test Reviewer",
                    "system_prompt": "Review.",
                    "provider": "claude-cli",
                    "model": "claude-sonnet-4-6"
                    {{toolNamesFragment}}
                }
            ]
        }
        """;

    private static string MakeAdvisorJson(string toolNamesFragment) => $$"""
        {
            "mode": "new",
            "domain": "general",
            "rationale": "test",
            "advisors": [
                {
                    "name": "test-advisor",
                    "display_name": "Test Advisor",
                    "system_prompt": "Advise.",
                    "provider": "claude-cli",
                    "model": "claude-sonnet-4-6",
                    "advisor_mode": "Strategic",
                    "advisor_trigger": "BeforeFirstExecution"
                    {{toolNamesFragment}}
                }
            ]
        }
        """;

    private static string MakeFinalizerJson(string toolNamesFragment) => $$"""
        {
            "mode": "new",
            "domain": "general",
            "rationale": "test",
            "finalizers": [
                {
                    "name": "test-finalizer",
                    "display_name": "Test Finalizer",
                    "finalizer_type": "Transform"
                    {{toolNamesFragment}}
                }
            ]
        }
        """;

    // ── Executor ──────────────────────────────────────────────────────────────

    [Fact]
    public void Executor_WithToolNames_PopulatesToolNamesList()
    {
        var json = MakeExecutorJson(""", "tool_names": ["web-search", "code-exec"]""");

        var artifact = CrewSpecParser.Parse(json);

        Assert.NotNull(artifact.Executor);
        Assert.NotNull(artifact.Executor.ToolNames);
        Assert.Equal(["web-search", "code-exec"], artifact.Executor.ToolNames);
    }

    [Fact]
    public void Executor_WithoutToolNames_ToolNamesIsNull()
    {
        var json = MakeExecutorJson(string.Empty);

        var artifact = CrewSpecParser.Parse(json);

        Assert.NotNull(artifact.Executor);
        Assert.Null(artifact.Executor.ToolNames);
    }

    [Fact]
    public void Executor_WithEmptyToolNamesArray_ReturnsEmptyList()
    {
        var json = MakeExecutorJson(""", "tool_names": []""");

        var artifact = CrewSpecParser.Parse(json);

        Assert.NotNull(artifact.Executor);
        Assert.NotNull(artifact.Executor.ToolNames);
        Assert.Empty(artifact.Executor.ToolNames);
    }

    // ── Reviewer ──────────────────────────────────────────────────────────────

    [Fact]
    public void Reviewer_WithToolNames_PopulatesToolNamesList()
    {
        var json = MakeReviewerJson(""", "tool_names": ["fact-check"]""");

        var artifact = CrewSpecParser.Parse(json);

        var reviewer = Assert.Single(artifact.Reviewers);
        Assert.NotNull(reviewer.ToolNames);
        Assert.Equal(["fact-check"], reviewer.ToolNames);
    }

    [Fact]
    public void Reviewer_WithoutToolNames_ToolNamesIsNull()
    {
        var json = MakeReviewerJson(string.Empty);

        var artifact = CrewSpecParser.Parse(json);

        var reviewer = Assert.Single(artifact.Reviewers);
        Assert.Null(reviewer.ToolNames);
    }

    // ── Advisor ───────────────────────────────────────────────────────────────

    [Fact]
    public void Advisor_WithToolNames_PopulatesToolNamesList()
    {
        var json = MakeAdvisorJson(""", "tool_names": ["literature-search", "summarize"]""");

        var artifact = CrewSpecParser.Parse(json);

        var advisor = Assert.Single(artifact.Advisors);
        Assert.NotNull(advisor.ToolNames);
        Assert.Equal(["literature-search", "summarize"], advisor.ToolNames);
    }

    [Fact]
    public void Advisor_WithoutToolNames_ToolNamesIsNull()
    {
        var json = MakeAdvisorJson(string.Empty);

        var artifact = CrewSpecParser.Parse(json);

        var advisor = Assert.Single(artifact.Advisors);
        Assert.Null(advisor.ToolNames);
    }

    // ── Finalizer ─────────────────────────────────────────────────────────────

    [Fact]
    public void Finalizer_WithToolNames_PopulatesToolNamesList()
    {
        var json = MakeFinalizerJson(""", "tool_names": ["export-pdf"]""");

        var artifact = CrewSpecParser.Parse(json);

        var finalizer = Assert.Single(artifact.Finalizers);
        Assert.NotNull(finalizer.ToolNames);
        Assert.Equal(["export-pdf"], finalizer.ToolNames);
    }

    [Fact]
    public void Finalizer_WithoutToolNames_ToolNamesIsNull()
    {
        var json = MakeFinalizerJson(string.Empty);

        var artifact = CrewSpecParser.Parse(json);

        var finalizer = Assert.Single(artifact.Finalizers);
        Assert.Null(finalizer.ToolNames);
    }

    // ── Round-trip: JSON → artifact (demonstrates full parse path) ────────────

    [Fact]
    public void Parse_ExecutorWithToolNames_ArtifactCarriesAllThreeTools()
    {
        // This test demonstrates the full JSON → CrewSpecArtifact round-trip,
        // including multiple tool names on an executor actor.
        const string json = """
            {
                "mode": "new",
                "domain": "academic",
                "rationale": "Needs web access and citation tools.",
                "executor": {
                    "name": "research-executor",
                    "display_name": "Research Executor",
                    "system_prompt": "Produce a research summary.",
                    "provider": "claude-cli",
                    "model": "claude-opus-4-8",
                    "tool_names": ["web-search", "arxiv-search", "cite-formatter"]
                }
            }
            """;

        var artifact = CrewSpecParser.Parse(json);

        Assert.NotNull(artifact.Executor);
        var tools = artifact.Executor.ToolNames;
        Assert.NotNull(tools);
        Assert.Equal(3, tools.Count);
        Assert.Equal("web-search",      tools[0]);
        Assert.Equal("arxiv-search",    tools[1]);
        Assert.Equal("cite-formatter",  tools[2]);
    }
}
