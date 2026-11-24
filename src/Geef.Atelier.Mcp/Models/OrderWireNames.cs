using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Mcp.Models;

/// <summary>Names of order, quality and usage values as they appear in MCP answers (snake_case, stable). Unknown values throw instead of inventing a name.</summary>
internal static class OrderWireNames
{
    /// <summary>The display state as defined once in the core (<see cref="OrderStateResolver.ToWireName"/>).</summary>
    public static string ToWire(this OrderDisplayState state) => state.ToWireName();

    public static string ToWire(this SourceQuality quality) => quality switch
    {
        SourceQuality.Good => "good",
        SourceQuality.Partial => "partial",
        SourceQuality.Poor => "poor",
        SourceQuality.Unreadable => "unreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), quality, null),
    };

    public static string ToWire(this SourceUsageMode mode) => mode switch
    {
        SourceUsageMode.FullText => "full_text",
        SourceUsageMode.FullTextWithMap => "full_text_with_map",
        SourceUsageMode.Excluded => "excluded",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
