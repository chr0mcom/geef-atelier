using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.Intake;

/// <summary>
/// JSON schema of the clarifier answer (contract stage 2, 4.4). Deliberately wider than <c>QuestionGate</c>: a question that
/// is too long is dropped in code and measured, instead of failing the whole call as a schema error. Integers have no
/// minimum or maximum, because some providers reject them.
/// </summary>
internal static class ClarifierSchema
{
    /// <summary>Name of the schema; the gateway and the tests recognise the clarifier call by it.</summary>
    public const string Name = "clarify_order";

    /// <summary>The schema as JSON text.</summary>
    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["text_type", "language", "understanding", "is_revision", "length_scale", "gaps", "research_topics"],
          "properties": {
            "text_type": { "type": "string", "maxLength": 80 },
            "language": { "type": "string", "pattern": "^[a-z]{2}$" },
            "understanding": { "type": "string", "maxLength": 400 },
            "is_revision": { "type": "boolean" },
            "revision_file": { "type": ["integer", "null"] },
            "length_scale": { "type": "string", "enum": ["short_form", "standard", "long_form"] },
            "gaps": {
              "type": "array", "maxItems": 8,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["topic", "resolution", "user_can_know", "web_researchable", "impact", "assumption", "confidence"],
                "properties": {
                  "topic": { "type": "string", "enum": ["length", "audience", "purpose", "tone", "formality", "language", "sender", "signature", "facts", "format", "date", "scope", "other"] },
                  "resolution": { "type": "string", "enum": ["derived", "memory", "research", "assume", "ask"] },
                  "user_can_know": { "type": "boolean" },
                  "web_researchable": { "type": "boolean" },
                  "impact": { "type": "string", "enum": ["low", "high"] },
                  "assumption": { "type": "string", "maxLength": 200 },
                  "confidence": { "type": "string", "enum": ["sure", "unsure"] },
                  "question": {
                    "type": ["object", "null"],
                    "additionalProperties": false,
                    "required": ["text", "options"],
                    "properties": {
                      "text": { "type": "string", "maxLength": 300 },
                      "options": { "type": "array", "items": { "type": "string", "maxLength": 120 }, "maxItems": 6 },
                      "default_option": { "type": ["integer", "null"] },
                      "allow_free_text": { "type": "boolean" }
                    }
                  }
                }
              }
            },
            "research_topics": { "type": "array", "maxItems": 5, "items": { "type": "string", "maxLength": 120 } }
          }
        }
        """;

    /// <summary>The schema as tool definition for <c>StructuredOutput.Build</c>.</summary>
    public static LlmTool Tool { get; } = new()
    {
        Name = Name,
        Description = "Analysis of a writing order before the writer starts: text type, language, understanding, gaps and how each gap is filled.",
        InputSchema = ParseSchema(),
    };

    private static JsonElement ParseSchema()
    {
        using var document = JsonDocument.Parse(Json);
        return document.RootElement.Clone();
    }
}
