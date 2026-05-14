namespace Geef.Atelier.Core.Domain;

/// <summary>Identifies which type of pipeline actor produced a cost record.</summary>
public enum ActorType
{
    Executor = 0,
    Reviewer = 1,
    Advisor  = 2
}
