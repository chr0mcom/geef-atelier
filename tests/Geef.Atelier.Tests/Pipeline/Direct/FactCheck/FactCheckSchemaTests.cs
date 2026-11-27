using System.Text.Json;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

namespace Geef.Atelier.Tests.Pipeline.Direct.FactCheck;

public sealed class FactCheckSchemaTests
{
    private static readonly string[] LimitKeywords = ["maxLength", "minLength", "maxItems", "minItems", "maximum", "minimum", "exclusiveMaximum", "exclusiveMinimum", "pattern", "maxProperties"];

    [Fact]
    public void The_schema_limits_neither_lengths_nor_counts()
    {
        var found = Keywords(FactCheckSchema.Check).Where(LimitKeywords.Contains).ToList();

        Assert.Empty(found);
    }

    [Fact]
    public void Every_object_is_closed_and_requires_all_its_properties()
    {
        var objects = Objects(FactCheckSchema.Check).ToList();

        Assert.NotEmpty(objects);
        foreach (var element in objects)
        {
            Assert.False(element.GetProperty("additionalProperties").GetBoolean());
            var properties = element.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToList();
            var required = element.GetProperty("required").EnumerateArray().Select(r => r.GetString()!).Order().ToList();
            Assert.Equal(properties, required);
        }
    }

    [Fact]
    public void The_check_schema_has_the_four_kinds_and_nullable_quotes()
    {
        var item = FactCheckSchema.Check.GetProperty("properties").GetProperty("findings").GetProperty("items").GetProperty("properties");

        Assert.Equal(["unsupported", "instruction", "superseded", "source_conflict"],
            item.GetProperty("kind").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        foreach (var quote in new[] { "text_quote", "instruction_quote", "material_quote", "material_quote_2" })
            Assert.Equal(["string", "null"], item.GetProperty(quote).GetProperty("type").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal("string", item.GetProperty("what_is_wrong").GetProperty("type").GetString());
    }

    [Fact]
    public void Names_match_the_contract() => Assert.Equal("check_facts", FactCheckSchema.CheckName);

    private static IEnumerable<string> Keywords(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => p.Name == "properties"
            ? p.Value.EnumerateObject().SelectMany(child => Keywords(child.Value))
            : Keywords(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Keywords),
        _ => [],
    };

    private static IEnumerable<JsonElement> Objects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object")
                yield return element;
            foreach (var property in element.EnumerateObject())
                foreach (var inner in Objects(property.Value))
                    yield return inner;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var inner in Objects(item))
                    yield return inner;
        }
    }
}
