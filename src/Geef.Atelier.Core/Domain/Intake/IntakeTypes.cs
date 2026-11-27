namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>Whether orders are clarified before their first run (<c>Orders:IntakeMode</c>).</summary>
public enum IntakeMode { Off = 0, Shadow = 1, On = 2 }

/// <summary>How the briefing of a clarified order is built (<c>Intake:BriefMode</c>).</summary>
public enum BriefMode { AppendAnswers = 0, PreparedBrief = 1 }

/// <summary>Topic of a gap in the request; at most one question per topic. Values are only ever appended.</summary>
public enum GapTopic { Length = 0, Audience = 1, Purpose = 2, Tone = 3, Formality = 4, Language = 5, Sender = 6, Signature = 7, Facts = 8, Format = 9, Date = 10, Scope = 11, Other = 12 }

/// <summary>How the clarifier wants a gap to be filled.</summary>
public enum GapResolution { Derived = 0, Memory = 1, Research = 2, Assume = 3, Ask = 4 }

/// <summary>How much a wrong guess for a gap would hurt the text.</summary>
public enum GapImpact { Low = 0, High = 1 }

/// <summary>How sure the clarifier is of an assumption.</summary>
public enum AssumptionConfidence { Sure = 0, Unsure = 1 }

/// <summary>Length class of the text type; selects the options of the length template.</summary>
public enum LengthScale { ShortForm = 0, Standard = 1, LongForm = 2 }

/// <summary>
/// Who gave an answer. <see cref="Form"/> is an answer from the form Atelier sent to the MCP client (D-088): the client says the person filled
/// it in, but the server cannot see who did, so it is kept apart from <see cref="User"/>. For writing and checking it counts as an answer of the
/// person, because Atelier built the form itself and only offered its own options.
/// </summary>
public enum AnswerOrigin { User = 0, Client = 1, Memory = 2, Form = 3 }

/// <summary>Whether an answer should be remembered (stage 7; always <see cref="No"/> in stage 2).</summary>
public enum RememberChoice { No = 0, Always = 1, TextType = 2 }

/// <summary>How the intake of an order ended (<c>Orders.IntakeOutcome</c>).</summary>
public enum IntakeOutcome { Clarified = 0, Asked = 1, Answered = 2, Skipped = 3, AutoStarted = 4, Failed = 5 }

/// <summary>Status of the clarifier call stored in an <see cref="IntakeRecord"/>.</summary>
public enum IntakeRecordStatus { Ok = 0, Failed = 1, TimedOut = 2, Invalid = 3, QueueTimeout = 4 }

/// <summary>A question as the model proposed it, before <see cref="QuestionGate"/>.</summary>
public sealed record ProposedQuestion(string Text, IReadOnlyList<string> Options, int? DefaultIndex, bool AllowFreeText);

/// <summary>One gap the clarifier reported.</summary>
public sealed record IntakeGap(
    GapTopic Topic,
    GapResolution Resolution,
    bool UserCanKnow,
    bool WebResearchable,
    GapImpact Impact,
    string? Assumption,
    AssumptionConfidence Confidence,
    ProposedQuestion? Question);

/// <summary>The parsed answer of the clarifier call; strings are already trimmed and capped.</summary>
public sealed record ClarifierOutput(
    string TextType,
    string Language,
    string Understanding,
    bool IsRevision,
    int? RevisionFileNumber,
    LengthScale LengthScale,
    IReadOnlyList<IntakeGap> Gaps,
    IReadOnlyList<string> ResearchTopics);

/// <summary>A clickable answer option. Ids are <c>o1…o4</c>, or fixed ids for template questions.</summary>
public sealed record QuestionOption(string Id, string Label);

/// <summary>
/// A question shown to the person (wire: <c>{id, slot, text, options:[{id,label}], default_option_id, allow_free_text}</c>).
/// <see cref="DefaultOptionId"/> is never set by the gate and never passed on; older stored records may still carry one.
/// </summary>
public sealed record ClarifyQuestion(
    string Id,
    GapTopic Slot,
    string Text,
    IReadOnlyList<QuestionOption> Options,
    string? DefaultOptionId,
    bool AllowFreeText,
    bool FromTemplate);

/// <summary>An assumption the writer will use unless the person changes it. Ids are <c>a1…</c>.</summary>
public sealed record IntakeAssumption(string Id, GapTopic Topic, string Text, AssumptionConfidence Confidence);

/// <summary>
/// An answer (wire: <c>{question_id, option_id?, free_text?, decide_for_me, origin, remember}</c>). <see cref="QuestionId"/>
/// names a question (<c>q1…</c>, <c>length</c>), an assumption (<c>a1…</c>, then <see cref="FreeText"/> is the correction)
/// or the revision guess (<c>revision</c>, option <c>yes</c>/<c>no</c>).
/// </summary>
public sealed record OrderAnswer(
    string QuestionId,
    string? OptionId,
    string? FreeText,
    bool DecideForMe,
    AnswerOrigin Origin,
    RememberChoice Remember = RememberChoice.No);

/// <summary>Fixed ids of the answer to the revision guess: question <see cref="QuestionId"/>, options <see cref="Yes"/> and <see cref="No"/>.</summary>
public static class RevisionAnswer
{
    /// <summary>Question id of the guess that a file is the text to improve.</summary>
    public const string QuestionId = "revision";

    /// <summary>Option "yes, improve the file".</summary>
    public const string Yes = "yes";

    /// <summary>Option "no, write something new instead".</summary>
    public const string No = "no";
}

/// <summary>A gap answered from the profile (stage 7: also from remembered answers).</summary>
public sealed record MemoryAnswer(GapTopic Topic, string Value, string Source);

/// <summary>What <see cref="QuestionGate"/> decided for one gap.</summary>
public enum GateDecision
{
    Asked = 0, Derived = 1, FromMemory = 2, ToResearch = 3, Assumed = 4,
    DroppedUserCannotKnow = 5, DroppedLowImpact = 6, DroppedRevisionRule = 7, DroppedLengthKnown = 8,
    DroppedLengthSure = 9, DroppedLengthNoTemplate = 10, DroppedTooLong = 11, DroppedJargon = 12,
    DroppedBadOptions = 13, DroppedLanguageMismatch = 14, DroppedDuplicateTopic = 15, DroppedOverLimit = 16,
    DroppedUnsafeContent = 17,
}

/// <summary>One entry of the gate trace: the gap by its position in the clarifier output and the decision.</summary>
public sealed record GateTrace(int GapIndex, GapTopic Topic, GateDecision Decision);

/// <summary>
/// Everything the intake step decided; stored as <c>Orders.ClarifyJson</c> and <c>OrderIntakeShadows.RecordJson</c>.
/// <see cref="HeldBack"/> are the questions about a possible decision of the person that the gate did not ask
/// (<see cref="QuestionGateResult.HeldBack"/>); <c>null</c> when there are none and for records written before they existed.
/// <see cref="ResearchWithheld"/> counts the research topics left out of <see cref="ResearchTopics"/> because they hold a word of the
/// About me details the clarifier read (<see cref="Geef.Atelier.Core.Domain.Research.ResearchQueryGate"/>); the research checks the topics again against the
/// details of the run, which may have changed in between. <c>null</c> when none was left out and for records written before.
/// <see cref="ResearchAllowed"/> is <c>true</c> only when looking things up on the web was switched on for the person of a standard
/// order when the request was read; only such a record may ever lead to a search (also when the switch changes later or a run is
/// resumed). <c>null</c> otherwise and for records written before it existed.
/// </summary>
public sealed record IntakeRecord(
    int SchemaVersion,
    IntakeRecordStatus Status,
    string? Model,
    int DurationMs,
    string? ErrorCode,
    ClarifierOutput? Output,
    IReadOnlyList<GateTrace> Trace,
    IReadOnlyList<ClarifyQuestion> Questions,
    IReadOnlyList<IntakeAssumption> Assumptions,
    IReadOnlyList<MemoryAnswer> FromMemory,
    IReadOnlyList<string> ResearchTopics,
    Guid? RevisionSourceId,
    BriefMode BriefMode,
    IReadOnlyList<string>? HeldBack = null,
    int? ResearchWithheld = null,
    bool? ResearchAllowed = null)
{
    /// <summary>Schema version written by this code.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Text of the single assumption of a <see cref="Fallback"/> record.</summary>
    public const string NotCheckedAssumption = "The request was not checked for missing details; the writer works from it as written.";

    /// <summary>Record of a failed or skipped intake: no questions, one assumption "not checked".</summary>
    public static IntakeRecord Fallback(IntakeRecordStatus status, string? errorCode, int durationMs, string? model, BriefMode briefMode) =>
        new(
            CurrentSchemaVersion,
            status,
            model,
            Math.Max(0, durationMs),
            errorCode,
            null,
            [],
            [],
            [new IntakeAssumption("a1", GapTopic.Other, NotCheckedAssumption, AssumptionConfidence.Unsure)],
            [],
            [],
            null,
            briefMode);
}
