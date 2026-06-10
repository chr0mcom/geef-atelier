namespace Geef.Atelier.Core.Domain.Tools;

/// <summary>Describes the result of a single tool invocation during a run.</summary>
public enum ToolInvocationOutcome
{
    /// <summary>The tool executed successfully and returned a result.</summary>
    Success = 0,

    /// <summary>The tool execution failed with an error.</summary>
    Failed = 1,

    /// <summary>The invocation was not executed because a cost or quota cap was reached.</summary>
    CapReached = 2,

    /// <summary>The invocation was blocked by a policy or safety check.</summary>
    Blocked = 3
}
