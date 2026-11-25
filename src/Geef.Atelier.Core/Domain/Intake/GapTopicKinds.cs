namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>How the assumptions of the clarifier about facts of the case reach the writer (<c>Intake:ContentAssumptions</c>).</summary>
public enum ContentAssumptionMode { Defaults = 0, OpenPoints = 1 }

/// <summary>Sorts gap topics into form (length, tone, …) and content (facts of the case).</summary>
public static class GapTopicKinds
{
    /// <summary>
    /// Topics about facts of the case (facts, date, sender, signature, other); their assumptions are never defaults for the writer.
    /// Only the known form topics count as form, so a topic added later is content until it is sorted here: a form assumption
    /// treated as content only becomes a placeholder, while a content assumption treated as form could end up stated as a fact.
    /// </summary>
    public static bool IsContent(this GapTopic topic) => topic is not (GapTopic.Length or GapTopic.Audience or GapTopic.Purpose
        or GapTopic.Tone or GapTopic.Formality or GapTopic.Language or GapTopic.Format or GapTopic.Scope);
}
