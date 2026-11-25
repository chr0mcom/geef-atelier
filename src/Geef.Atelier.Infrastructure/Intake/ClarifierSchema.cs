using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.Intake;

/// <summary>
/// JSON schema of the clarifier answer (contract stage 2, 4.4). Deliberately wider than <c>QuestionGate</c>: the gateway
/// rejects the whole answer on any schema violation, so texts have no length limit and the list limits are generous. The
/// parser caps every field, and a question that is too long is dropped in code and measured. Integers have no minimum or
/// maximum, because some providers reject them.
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
            "text_type": { "type": "string" },
            "language": { "type": "string", "pattern": "^[a-z]{2}$" },
            "understanding": { "type": "string" },
            "is_revision": { "type": "boolean" },
            "revision_file": { "type": ["integer", "null"] },
            "length_scale": { "type": "string", "enum": ["short_form", "standard", "long_form"] },
            "gaps": {
              "type": "array", "maxItems": 20,
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
                  "assumption": { "type": "string" },
                  "confidence": { "type": "string", "enum": ["sure", "unsure"] },
                  "question": {
                    "type": ["object", "null"],
                    "additionalProperties": false,
                    "required": ["text", "options"],
                    "properties": {
                      "text": { "type": "string" },
                      "options": { "type": "array", "items": { "type": "string" }, "maxItems": 12 },
                      "default_option": { "type": ["integer", "null"] },
                      "allow_free_text": { "type": "boolean" }
                    }
                  }
                }
              }
            },
            "research_topics": { "type": "array", "maxItems": 12, "items": { "type": "string" } }
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
