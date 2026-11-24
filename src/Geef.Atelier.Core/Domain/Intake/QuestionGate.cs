namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>Input of <see cref="QuestionGate.Apply"/>.</summary>
public sealed record QuestionGateInput(
    ClarifierOutput Output,
    string RequestText,
    bool HasFiles,
    bool RevisionDetected,
    IReadOnlyDictionary<GapTopic, MemoryAnswer> Known,
    int MaxQuestions);

/// <summary>What is left after the gate: the questions to show, the assumptions, the profile answers, the research topics and the trace.</summary>
public sealed record QuestionGateResult(
    IReadOnlyList<ClarifyQuestion> Questions,
    IReadOnlyList<IntakeAssumption> Assumptions,
    IReadOnlyList<MemoryAnswer> FromMemory,
    IReadOnlyList<string> ResearchTopics,
    IReadOnlyList<GateTrace> Trace);

/// <summary>Enforces the question rule in code: few, short, plain questions with clickable answers. Pure function.</summary>
public static class QuestionGate
{
    /// <summary>Longest question text shown to the person.</summary>
    public const int MaxQuestionChars = 120;

    /// <summary>Longest answer option.</summary>
    public const int MaxOptionChars = 60;

    /// <summary>Fewest answer options of a question.</summary>
    public const int MinOptions = 2;

    /// <summary>Most answer options of a question.</summary>
    public const int MaxOptions = 4;

    /// <summary>Most assumptions shown to the person.</summary>
    public const int MaxAssumptions = 8;

    /// <summary>Applies the gate rules gap by gap in fixed order; every decision is recorded in the trace.</summary>
    public static QuestionGateResult Apply(QuestionGateInput input) => throw new NotImplementedException();
}
