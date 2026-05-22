using System.Text.Json;
using Geef.Atelier.Infrastructure.Grounding;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

/// <summary>
/// Tests for <see cref="JsonPathNavigator"/> — covers all documented segment types:
/// property names, nested paths, array wildcard, array index, root-only path.
/// </summary>
public sealed class JsonPathNavigatorTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ── root path ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("$")]
    [InlineData("")]
    [InlineData("  ")]
    public void Select_RootOrEmpty_ReturnsRootElement(string path)
    {
        var root = Parse("""{"a":1}""");
        var result = JsonPathNavigator.Select(root, path);
        Assert.Single(result);
        Assert.Equal(JsonValueKind.Object, result[0].ValueKind);
    }

    // ── simple property ───────────────────────────────────────────────────────

    [Fact]
    public void Select_DotField_ReturnsPropertyValue()
    {
        var root = Parse("""{"name":"Alice","age":30}""");
        var result = JsonPathNavigator.Select(root, "$.name");
        Assert.Single(result);
        Assert.Equal("Alice", result[0].GetString());
    }

    [Fact]
    public void Select_MissingProperty_ReturnsEmpty()
    {
        var root = Parse("""{"name":"Alice"}""");
        var result = JsonPathNavigator.Select(root, "$.missing");
        Assert.Empty(result);
    }

    // ── nested path ───────────────────────────────────────────────────────────

    [Fact]
    public void Select_NestedPath_ReturnsDeepValue()
    {
        var root = Parse("""{"meta":{"author":"Bob","year":2024}}""");
        var result = JsonPathNavigator.Select(root, "$.meta.author");
        Assert.Single(result);
        Assert.Equal("Bob", result[0].GetString());
    }

    [Fact]
    public void Select_NestedPath_MissingIntermediate_ReturnsEmpty()
    {
        var root = Parse("""{"other":"x"}""");
        var result = JsonPathNavigator.Select(root, "$.meta.author");
        Assert.Empty(result);
    }

    // ── array wildcard ────────────────────────────────────────────────────────

    [Fact]
    public void Select_ArrayWildcard_ReturnsAllElements()
    {
        var root = Parse("""{"items":["a","b","c"]}""");
        var result = JsonPathNavigator.Select(root, "$.items[*]");
        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0].GetString());
        Assert.Equal("c", result[2].GetString());
    }

    [Fact]
    public void Select_ArrayWildcardDotProp_ReturnsPropertyFromEachElement()
    {
        var root = Parse("""{"records":[{"id":1,"name":"X"},{"id":2,"name":"Y"}]}""");
        var result = JsonPathNavigator.Select(root, "$.records[*].name");
        Assert.Equal(2, result.Count);
        Assert.Equal("X", result[0].GetString());
        Assert.Equal("Y", result[1].GetString());
    }

    [Fact]
    public void Select_ArrayWildcard_OnNonArray_ReturnsEmpty()
    {
        var root = Parse("""{"items":"not-an-array"}""");
        var result = JsonPathNavigator.Select(root, "$.items[*]");
        Assert.Empty(result);
    }

    // ── array index ───────────────────────────────────────────────────────────

    [Fact]
    public void Select_ArrayIndex0_ReturnsFirstElement()
    {
        var root = Parse("""{"data":[10,20,30]}""");
        var result = JsonPathNavigator.Select(root, "$.data[0]");
        Assert.Single(result);
        Assert.Equal(10, result[0].GetInt32());
    }

    [Fact]
    public void Select_ArrayIndex2_ReturnsThirdElement()
    {
        var root = Parse("""{"data":["a","b","c"]}""");
        var result = JsonPathNavigator.Select(root, "$.data[2]");
        Assert.Single(result);
        Assert.Equal("c", result[0].GetString());
    }

    [Fact]
    public void Select_ArrayIndexOutOfBounds_ReturnsEmpty()
    {
        var root = Parse("""{"data":[1,2]}""");
        var result = JsonPathNavigator.Select(root, "$.data[5]");
        Assert.Empty(result);
    }

    // ── numeric values ────────────────────────────────────────────────────────

    [Fact]
    public void Select_NumericField_ReturnsNumber()
    {
        var root = Parse("""{"count":42}""");
        var result = JsonPathNavigator.Select(root, "$.count");
        Assert.Single(result);
        Assert.Equal(42, result[0].GetInt32());
    }

    // ── empty array ────────────────────────────────────────────────────────────

    [Fact]
    public void Select_WildcardOnEmptyArray_ReturnsEmpty()
    {
        var root = Parse("""{"items":[]}""");
        var result = JsonPathNavigator.Select(root, "$.items[*]");
        Assert.Empty(result);
    }
}
