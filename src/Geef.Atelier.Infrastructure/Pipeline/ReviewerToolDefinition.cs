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
                                "enum": ["info", "warning", "error", "critical"]
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

    public static readonly AnthropicTool SubmitReview = new()
    {
        Name = "submit_review",
        Description = "Submit structured review results. Set approved=true only when the findings array is empty.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>(SchemaJson)
    };
}
