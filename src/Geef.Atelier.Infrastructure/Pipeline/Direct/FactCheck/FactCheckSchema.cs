using System.Text.Json;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

/// <summary>
/// JSON schemas of the fact check (strict, contract stage 3, 4.4). They have no length or count limits, because the gateway
/// rejects the whole answer on any schema violation; the parser caps instead.
/// </summary>
internal static class FactCheckSchema
{
    public const string CheckName = "check_facts";
    public const string RecheckName = "recheck_facts";

    public const string CheckJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["findings"],
          "properties": {
            "findings": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["kind", "text_quote", "what_is_wrong", "instruction_quote", "material_quote", "material_quote_2"],
                "properties": {
                  "kind": { "type": "string", "enum": ["unsupported", "instruction", "superseded", "source_conflict"] },
                  "text_quote": { "type": ["string", "null"] },
                  "what_is_wrong": { "type": "string" },
                  "instruction_quote": { "type": ["string", "null"] },
                  "material_quote": { "type": ["string", "null"] },
                  "material_quote_2": { "type": ["string", "null"] }
                }
              }
            }
          }
        }
        """;

    public const string RecheckJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["results"],
          "properties": {
            "results": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "required": ["id", "status", "text_quote"],
                "properties": {
                  "id": { "type": "string" },
                  "status": { "type": "string", "enum": ["fixed", "still_there"] },
                  "text_quote": { "type": ["string", "null"] }
                }
              }
            }
          }
        }
        """;

    /// <summary>Schema of the <c>check_facts</c> answer.</summary>
    public static JsonElement Check { get; } = Parse(CheckJson);

    /// <summary>Schema of the <c>recheck_facts</c> answer.</summary>
    public static JsonElement Recheck { get; } = Parse(RecheckJson);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
