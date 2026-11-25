using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>
/// JSON schema of the passages answer. It has no length or count limits, because the gateway rejects the whole answer on any schema
/// violation; the parser drops instead (a quote is never shortened).
/// </summary>
internal static class DossierSchema
{
    /// <summary>Name of the schema; the gateway and the tests recognise the call by it.</summary>
    public const string Name = "pick_passages";

    /// <summary>The schema as JSON text.</summary>
    public const string Json = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["passages"],
          "properties": {
            "passages": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["topic", "page", "quote"],
                "properties": {
                  "topic": { "type": "integer" },
                  "page": { "type": "string" },
                  "quote": { "type": "string" }
                }
              }
            }
          }
        }
        """;

    /// <summary>The schema as tool definition for <c>StructuredOutput.Build</c>.</summary>
    public static LlmTool Tool { get; } = new()
    {
        Name = Name,
        Description = "Passages copied word for word from the given web pages, each with the number of the topic it answers and the id of its page.",
        InputSchema = ParseSchema(),
    };

    private static JsonElement ParseSchema()
    {
        using var document = JsonDocument.Parse(Json);
        return document.RootElement.Clone();
    }
}
