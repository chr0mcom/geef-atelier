using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class ReviewerToolDefinition
{
    private const string SchemaJson = """
        {
            "type": "object",
            "properties": {
                "approved": {
                    "type": "boolean"
                },
                "findings": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "severity": {
                                "type": "string",
                                "enum": ["critical", "major", "minor", "info"]
                            },
                            "message": {
                                "type": "string"
                            }
                        },
                        "required": ["severity", "message"]
                    }
                }
            },
            "required": ["approved", "findings"]
        }
        """;

    public static readonly LlmTool SubmitReview = new()
    {
        Name = "submit_review",
        Description = "Submit structured review results. severity must be one of: critical (factual error), major (important omission), minor (style/precision), info (observation). Convergence is decided by severity: a run is rejected ONLY when at least one critical or major finding exists. minor and info findings do NOT block — approve the draft (approved=true) when it has no critical or major issues, even if you still list minor/info findings. Always include at least one finding.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>(SchemaJson)
    };
}
