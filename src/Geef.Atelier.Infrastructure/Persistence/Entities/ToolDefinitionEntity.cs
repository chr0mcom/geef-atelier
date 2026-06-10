using System.Text.Json;
using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Infrastructure.Persistence.Entities;

/// <summary>
/// EF Core mapping entity for the <c>tool_definitions</c> table.
/// Maps to and from the immutable <see cref="ToolDefinition"/> domain record.
/// </summary>
internal sealed class ToolDefinitionEntity
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string ToolType { get; set; } = "";
    public Dictionary<string, string> Settings { get; set; } = new();
    public string? SecretRef { get; set; }
    public string LlmSchemaJson { get; set; } = "{}";
    public int AccessClass { get; set; }
    public bool IsSystem { get; set; }

    /// <summary>Maps this entity to the immutable domain record.</summary>
    public ToolDefinition ToDomain() => new(
        Name,
        DisplayName,
        Description,
        ToolType,
        Settings.AsReadOnly(),
        SecretRef,
        JsonDocument.Parse(LlmSchemaJson).RootElement,
        (ToolAccessClass)AccessClass,
        IsSystem);

    /// <summary>Creates an entity from a domain record.</summary>
    public static ToolDefinitionEntity FromDomain(ToolDefinition d) => new()
    {
        Name = d.Name,
        DisplayName = d.DisplayName,
        Description = d.Description,
        ToolType = d.ToolType,
        Settings = new Dictionary<string, string>(d.Settings),
        SecretRef = d.SecretRef,
        LlmSchemaJson = d.LlmSchema.GetRawText(),
        AccessClass = (int)d.AccessClass,
        IsSystem = d.IsSystem
    };
}
