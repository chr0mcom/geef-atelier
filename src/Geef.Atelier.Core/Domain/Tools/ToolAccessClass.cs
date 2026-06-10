namespace Geef.Atelier.Core.Domain.Tools;

/// <summary>Describes the side-effect class of a tool invocation.</summary>
public enum ToolAccessClass
{
    /// <summary>The tool only reads data and does not mutate external state.</summary>
    ReadOnly = 0,

    /// <summary>The tool writes, creates, or otherwise mutates external state.</summary>
    Mutating = 1
}
