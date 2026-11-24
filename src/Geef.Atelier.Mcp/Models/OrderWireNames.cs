using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Mcp.Models;

/// <summary>Names of order, quality and usage values as they appear in MCP answers (snake_case, stable).</summary>
internal static class OrderWireNames
{
    public static string ToWire(this OrderDisplayState state) => state switch
    {
        OrderDisplayState.Preparing => "preparing",
        OrderDisplayState.NeedsInput => "needs_input",
        OrderDisplayState.Queued => "queued",
        OrderDisplayState.Working => "working",
        OrderDisplayState.Delivered => "delivered",
        OrderDisplayState.DeliveredWithOpenPoints => "delivered_with_open_points",
        OrderDisplayState.Failed => "failed",
        OrderDisplayState.Stopped => "stopped",
        _ => state.ToString().ToLowerInvariant(),
    };

    public static string ToWire(this SourceQuality quality) => quality switch
    {
        SourceQuality.Good => "good",
        SourceQuality.Partial => "partial",
        SourceQuality.Poor => "poor",
        SourceQuality.Unreadable => "unreadable",
        _ => quality.ToString().ToLowerInvariant(),
    };

    public static string ToWire(this SourceUsageMode mode) => mode switch
    {
        SourceUsageMode.FullText => "full_text",
        SourceUsageMode.FullTextWithMap => "full_text_with_map",
        SourceUsageMode.Excluded => "excluded",
        _ => mode.ToString().ToLowerInvariant(),
    };
}
