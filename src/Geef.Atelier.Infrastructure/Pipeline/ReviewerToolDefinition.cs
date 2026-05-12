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
        Description = "Submit structured review results. severity must be one of: critical (factual error), major (important omission), minor (style/precision), info (observation). Set approved=true only when findings is empty.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>(SchemaJson)
    };
}
