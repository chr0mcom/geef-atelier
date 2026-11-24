namespace Geef.Atelier.Core.Configuration;

/// <summary>
/// Switch for the continuous-learning loop (extraction finalizer, learning-evaluation runs, publication and
/// learning retrieval). While disabled, the loop does nothing: no extraction call, no learning run, no embedding,
/// empty retrieval context. Stored learnings and their tables stay untouched.
/// </summary>
public sealed class LearningOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Learning";

    /// <summary>Whether the learning loop runs. Default false (paused since 2026-11-24).</summary>
    public bool Enabled { get; set; }
}
