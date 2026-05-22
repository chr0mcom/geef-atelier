using System.Text.Json;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Lightweight JSONPath evaluator for common patterns using <see cref="JsonElement"/>.
/// Supports: <c>$</c>, <c>$.field</c>, <c>$.a.b</c>, <c>$.a[*]</c>, <c>$.a[*].b</c>, <c>$.a[0]</c>.
/// Returns an empty list when the path is absent or invalid.
/// </summary>
internal static class JsonPathNavigator
{
    /// <summary>
    /// Evaluates <paramref name="path"/> against <paramref name="root"/> and returns all matching elements.
    /// </summary>
    public static IReadOnlyList<JsonElement> Select(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "$")
            return [root];

        var segments = Tokenize(path);
        if (segments.Count == 0)
            return [];

        var current = new List<JsonElement> { root };
        foreach (var segment in segments)
        {
            if (current.Count == 0)
                break;
            current = ApplySegment(current, segment);
        }

        return current;
    }

    private static List<string> Tokenize(string path)
    {
        // Strip leading "$."  or "$"
        var s = path.TrimStart();
        if (s.StartsWith("$.", StringComparison.Ordinal))
            s = s[2..];
        else if (s.StartsWith("$[", StringComparison.Ordinal))
            s = s[1..];
        else if (s.StartsWith("$", StringComparison.Ordinal))
            s = s[1..];

        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '[')
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
                var end = s.IndexOf(']', i + 1);
                if (end < 0) break;
                parts.Add("[" + s[(i + 1)..end] + "]");
                i = end;
            }
            else if (c == '.')
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            parts.Add(current.ToString());

        return parts;
    }

    private static List<JsonElement> ApplySegment(List<JsonElement> nodes, string segment)
    {
        var result = new List<JsonElement>();
        foreach (var node in nodes)
        {
            if (segment.StartsWith('[') && segment.EndsWith(']'))
            {
                var inner = segment[1..^1];
                if (inner == "*")
                {
                    // Array wildcard
                    if (node.ValueKind == JsonValueKind.Array)
                        foreach (var item in node.EnumerateArray())
                            result.Add(item);
                }
                else if (int.TryParse(inner, out var idx))
                {
                    // Array index
                    if (node.ValueKind == JsonValueKind.Array)
                    {
                        var arr = node.EnumerateArray().ToList();
                        var realIdx = idx < 0 ? arr.Count + idx : idx;
                        if (realIdx >= 0 && realIdx < arr.Count)
                            result.Add(arr[realIdx]);
                    }
                }
            }
            else
            {
                // Property name
                if (node.ValueKind == JsonValueKind.Object &&
                    node.TryGetProperty(segment, out var prop))
                    result.Add(prop);
            }
        }
        return result;
    }
}
