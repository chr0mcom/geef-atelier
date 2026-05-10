namespace Geef.Atelier.Core.Domain;

/// <summary>Lifecycle status of a text-generation run.</summary>
public enum RunStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Aborted,
}
