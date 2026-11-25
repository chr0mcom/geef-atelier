using System.Text.Json;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

namespace Geef.Atelier.Tests.Pipeline.Direct.FactCheck;

public sealed class FactCheckSchemaTests
{
    private static readonly string[] LimitKeywords = ["maxLength", "minLength", "maxItems", "minItems", "maximum", "minimum", "exclusiveMaximum", "exclusiveMinimum", "pattern", "maxProperties"];

    public static TheoryData<string> Schemas => new() { FactCheckSchema.CheckName, FactCheckSchema.RecheckName };

    private static JsonElement Schema(string name) => name == FactCheckSchema.CheckName ? FactCheckSchema.Check : FactCheckSchema.Recheck;

    [Theory]
    [MemberData(nameof(Schemas))]
    public void No_schema_limits_lengths_or_counts(string name)
    {
        var found = Keywords(Schema(name)).Where(LimitKeywords.Contains).ToList();

        Assert.Empty(found);
    }

    [Theory]
    [MemberData(nameof(Schemas))]
    public void Every_object_is_closed_and_requires_all_its_properties(string name)
    {
        var objects = Objects(Schema(name)).ToList();

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
    public void The_recheck_schema_answers_fixed_or_still_there()
    {
        var item = FactCheckSchema.Recheck.GetProperty("properties").GetProperty("results").GetProperty("items").GetProperty("properties");

        Assert.Equal(["fixed", "still_there"], item.GetProperty("status").GetProperty("enum").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["string", "null"], item.GetProperty("text_quote").GetProperty("type").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public void Names_match_the_contract() =>
        Assert.Equal(("check_facts", "recheck_facts"), (FactCheckSchema.CheckName, FactCheckSchema.RecheckName));

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
