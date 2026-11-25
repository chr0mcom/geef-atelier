using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>Input of <see cref="QuestionGate.Apply"/>.</summary>
public sealed record QuestionGateInput(
    ClarifierOutput Output,
    string RequestText,
    bool RevisionDetected,
    IReadOnlyDictionary<GapTopic, MemoryAnswer> Known,
    int MaxQuestions);

/// <summary>
/// What is left after the gate: the questions to show, the assumptions, the profile answers, the research topics, the trace
/// and the summary of the request (<see cref="Understanding"/>, empty when the model's summary must not be shown).
/// </summary>
public sealed record QuestionGateResult(
    IReadOnlyList<ClarifyQuestion> Questions,
    IReadOnlyList<IntakeAssumption> Assumptions,
    IReadOnlyList<MemoryAnswer> FromMemory,
    IReadOnlyList<string> ResearchTopics,
    IReadOnlyList<GateTrace> Trace,
    string Understanding);

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

    /// <summary>Most questions whatever the configuration says.</summary>
    public const int HardMaxQuestions = 3;

    /// <summary>Most research topics kept.</summary>
    public const int MaxResearchTopics = 5;

    private const int MaxResearchTopicChars = 120;

    private static readonly HashSet<GapTopic> ProfileTopics = [GapTopic.Language, GapTopic.Sender, GapTopic.Signature, GapTopic.Formality];
    private static readonly HashSet<GapTopic> RevisionTopics = [GapTopic.Length, GapTopic.Tone, GapTopic.Formality];
    private static readonly HashSet<GapTopic> LengthCarrierTopics = [GapTopic.Format, GapTopic.Scope, GapTopic.Other];
    private static readonly HashSet<GapTopic> BroadTopics = [GapTopic.Facts, GapTopic.Other];

    /// <summary>
    /// Applies the gate rules gap by gap in fixed order; every decision is recorded in the trace. A question under
    /// <see cref="GapTopic.Format"/>, <see cref="GapTopic.Scope"/> or <see cref="GapTopic.Other"/> that asks for the length of the
    /// text itself counts as topic <see cref="GapTopic.Length"/>, so that only the template asks for it. A narrow topic that is asked
    /// gets no assumption from a dropped duplicate or another gap, so that the brief never sets a guess beside the answer; a broad
    /// topic (<see cref="GapTopic.Facts"/>, <see cref="GapTopic.Other"/>) covers different matters, so a dropped duplicate keeps the
    /// assumption the model gave (never a generated "not specified" line). Questions, options, assumptions and the summary with links, addresses or secrets are dropped (see
    /// <see cref="IntakeContentGuard.IsUnsafe"/>; the request of the person decides which bare names and secret topics are fine).
    /// </summary>
    public static QuestionGateResult Apply(QuestionGateInput input)
    {
        var output = input.Output;
        var gaps = output.Gaps.Select(AsLengthGap).ToList();
        var language = (output.Language ?? string.Empty).Trim().ToLowerInvariant();
        var revision = input.RevisionDetected || output.IsRevision;
        var lengthKnown = new Lazy<bool>(() => LengthInRequest(input.RequestText));
        var decisions = new GateDecision[gaps.Count];
        var candidates = new List<(int Index, ClarifyQuestion Question)>();
        var fromMemory = new List<MemoryAnswer>();

        for (var i = 0; i < gaps.Count; i++)
        {
            var (decision, question) = Decide(gaps[i], input, language, revision, lengthKnown);
            decisions[i] = decision;
            if (question is not null)
                candidates.Add((i, question));
            if (decision == GateDecision.FromMemory && input.Known.TryGetValue(gaps[i].Topic, out var known) && fromMemory.All(m => m.Topic != known.Topic))
                fromMemory.Add(known);
        }

        var unique = new List<(int Index, ClarifyQuestion Question)>();
        var topics = new HashSet<GapTopic>();
        foreach (var candidate in candidates)
        {
            if (topics.Add(candidate.Question.Slot))
                unique.Add(candidate);
            else
                decisions[candidate.Index] = GateDecision.DroppedDuplicateTopic;
        }

        var limit = Math.Clamp(input.MaxQuestions, 0, HardMaxQuestions);
        if (revision)
            limit = Math.Min(limit, 1);
        var ordered = unique.Where(c => c.Question.Slot == GapTopic.Length).Concat(unique.Where(c => c.Question.Slot != GapTopic.Length)).ToList();
        var kept = ordered.Take(limit).ToList();
        foreach (var dropped in ordered.Skip(limit))
            decisions[dropped.Index] = GateDecision.DroppedOverLimit;
        foreach (var asked in kept)
            decisions[asked.Index] = GateDecision.Asked;

        var number = 0;
        var questions = kept
            .Select(c => c.Question.FromTemplate ? c.Question : c.Question with { Id = $"q{++number}" })
            .ToList();

        return new QuestionGateResult(
            questions,
            Assumptions(gaps, decisions, input.RequestText, lengthKnown),
            fromMemory,
            ResearchTopics(output, gaps, decisions, input.RequestText),
            gaps.Select((g, i) => new GateTrace(i, g.Topic, decisions[i])).ToList(),
            IntakeContentGuard.IsUnsafe(output.Understanding, input.RequestText) ? string.Empty : output.Understanding ?? string.Empty);
    }

    private static IntakeGap AsLengthGap(IntakeGap gap) =>
        LengthCarrierTopics.Contains(gap.Topic) && LengthMentions.AsksForTextLength(gap.Question) ? gap with { Topic = GapTopic.Length } : gap;

    /// <summary>Plain English name of a topic, used in generated assumptions.</summary>
    public static string Label(this GapTopic topic) => topic switch
    {
        GapTopic.Length => "Length",
        GapTopic.Audience => "Readers",
        GapTopic.Purpose => "Purpose",
        GapTopic.Tone => "Tone",
        GapTopic.Formality => "Form of address",
        GapTopic.Language => "Language",
        GapTopic.Sender => "Sender",
        GapTopic.Signature => "Signature",
        GapTopic.Facts => "Facts",
        GapTopic.Format => "Format",
        GapTopic.Date => "Date",
        GapTopic.Scope => "Scope",
        _ => "Other details",
    };

    /// <summary>The assumption used for a dropped question when the model gave none.</summary>
    public static string NotSpecified(GapTopic topic) => $"{topic.Label()}: not specified – the writer decides";

    private static (GateDecision Decision, ClarifyQuestion? Question) Decide(
        IntakeGap gap, QuestionGateInput input, string language, bool revision, Lazy<bool> lengthKnown)
    {
        if (ProfileTopics.Contains(gap.Topic) && input.Known.ContainsKey(gap.Topic))
            return (GateDecision.FromMemory, null);

        switch (gap.Resolution)
        {
            case GapResolution.Derived:
                return (GateDecision.Derived, null);
            case GapResolution.Memory:
                return (input.Known.ContainsKey(gap.Topic) ? GateDecision.FromMemory : GateDecision.Assumed, null);
            case GapResolution.Research:
                return (GateDecision.ToResearch, null);
            case GapResolution.Assume:
                return (GateDecision.Assumed, null);
        }

        if (gap.WebResearchable)
            return (GateDecision.ToResearch, null);
        if (!gap.UserCanKnow)
            return (GateDecision.DroppedUserCannotKnow, null);
        if (gap.Impact == GapImpact.Low)
            return (GateDecision.DroppedLowImpact, null);
        if (revision && RevisionTopics.Contains(gap.Topic))
            return (GateDecision.DroppedRevisionRule, null);

        if (gap.Topic == GapTopic.Length)
        {
            if (lengthKnown.Value)
                return (GateDecision.DroppedLengthKnown, null);
            if (gap.Confidence == AssumptionConfidence.Sure)
                return (GateDecision.DroppedLengthSure, null);
            if (!QuestionTemplates.Supports(language))
                return (GateDecision.DroppedLengthNoTemplate, null);
            return (GateDecision.Asked, QuestionTemplates.Length(language, input.Output.LengthScale));
        }

        if (gap.Question is { } original && Unsafe(original, input.RequestText))
            return (GateDecision.DroppedUnsafeContent, null);

        var proposed = gap.Question is { } raw ? WithoutDeferrals(raw) : null;
        if (CheckForm(proposed, language) is { } rejected)
            return (rejected, null);

        var options = proposed!.Options.Select((o, i) => new QuestionOption($"o{i + 1}", o.Trim())).ToList();
        var defaultId = proposed.DefaultIndex is { } index && index >= 0 && index < options.Count ? options[index].Id : null;
        return (GateDecision.Asked, new ClarifyQuestion(string.Empty, gap.Topic, proposed.Text.Trim(), options, defaultId, proposed.AllowFreeText, false));
    }

    private static ProposedQuestion WithoutDeferrals(ProposedQuestion question)
    {
        var kept = question.Options
            .Select((text, index) => (Text: text, Index: index))
            .Where(o => !JargonBlocklist.IsDeferralOption(o.Text ?? string.Empty))
            .ToList();
        if (kept.Count == question.Options.Count)
            return question;
        var defaultIndex = kept.FindIndex(o => o.Index == question.DefaultIndex);
        return question with { Options = kept.Select(o => o.Text).ToList(), DefaultIndex = defaultIndex < 0 ? null : defaultIndex };
    }

    private static GateDecision? CheckForm(ProposedQuestion? question, string language)
    {
        var text = question?.Text?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.Length > MaxQuestionChars)
            return GateDecision.DroppedTooLong;

        var options = question!.Options.Select(o => o?.Trim() ?? string.Empty).ToList();
        if (options.Count < MinOptions || options.Count > MaxOptions
            || options.Any(o => o.Length == 0 || o.Length > MaxOptionChars)
            || options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count
            || options.Any(JargonBlocklist.IsFillerOption))
            return GateDecision.DroppedBadOptions;

        if (JargonBlocklist.Contains(text) || options.Any(JargonBlocklist.Contains))
            return GateDecision.DroppedJargon;

        var guess = TextLanguageGuess.Guess($"{text}\n{string.Join('\n', options)}");
        if (guess is not null && guess != language)
            return GateDecision.DroppedLanguageMismatch;

        return null;
    }

    private static bool Unsafe(ProposedQuestion question, string requestText) =>
        IntakeContentGuard.IsUnsafe(question.Text, requestText)
        || question.Options.Any(o => IntakeContentGuard.IsUnsafe(o, requestText))
        || IntakeContentGuard.AsksForSecret($"{question.Text}\n{string.Join('\n', question.Options)}");

    private static bool LengthInRequest(string requestText) =>
        !string.IsNullOrWhiteSpace(requestText)
        && (RequestConstraintExtractor.Extract(requestText).Length is not null
            || LengthRuleGate.MentionsLength(requestText)
            || LengthMentions.InRequest(requestText));

    private static IReadOnlyList<IntakeAssumption> Assumptions(IReadOnlyList<IntakeGap> gaps, IReadOnlyList<GateDecision> decisions, string requestText, Lazy<bool> lengthKnown)
    {
        var asked = gaps.Where((_, i) => decisions[i] == GateDecision.Asked).Select(g => g.Topic).ToHashSet();
        var drafts = new List<(GapTopic Topic, string Text, AssumptionConfidence Confidence)>();
        for (var i = 0; i < gaps.Count; i++)
        {
            var gap = gaps[i];
            var given = string.IsNullOrWhiteSpace(gap.Assumption) || IntakeContentGuard.IsUnsafe(gap.Assumption, requestText) ? null : gap.Assumption.Trim();
            var askedTopic = asked.Contains(gap.Topic);
            var text = decisions[i] switch
            {
                GateDecision.Asked or GateDecision.Derived or GateDecision.FromMemory => null,
                _ when gap.Topic == GapTopic.Length && lengthKnown.Value => null,
                GateDecision.DroppedDuplicateTopic when askedTopic => BroadTopics.Contains(gap.Topic) ? given : null,
                _ when askedTopic && !BroadTopics.Contains(gap.Topic) => null,
                GateDecision.Assumed when gap.Resolution == GapResolution.Memory => given ?? NotSpecified(gap.Topic),
                GateDecision.Assumed or GateDecision.ToResearch => given,
                GateDecision.DroppedRevisionRule => given,
                _ => given ?? NotSpecified(gap.Topic),
            };
            if (text is not null && drafts.All(d => !string.Equals(d.Text, text, StringComparison.OrdinalIgnoreCase)))
                drafts.Add((gap.Topic, text, gap.Confidence));
        }

        return drafts
            .Where(d => d.Confidence == AssumptionConfidence.Unsure)
            .Concat(drafts.Where(d => d.Confidence != AssumptionConfidence.Unsure))
            .Take(MaxAssumptions)
            .Select((d, i) => new IntakeAssumption($"a{i + 1}", d.Topic, d.Text, d.Confidence))
            .ToList();
    }

    private static IReadOnlyList<string> ResearchTopics(ClarifierOutput output, IReadOnlyList<IntakeGap> gaps, IReadOnlyList<GateDecision> decisions, string requestText)
    {
        var topics = output.ResearchTopics.Select(t => t?.Trim() ?? string.Empty).Where(t => t.Length > 0 && !IntakeContentGuard.IsUnsafe(t, requestText)).ToList();
        if (topics.Count == 0)
        {
            for (var i = 0; i < gaps.Count; i++)
            {
                if (decisions[i] != GateDecision.ToResearch)
                    continue;
                var question = gaps[i].Question?.Text?.Trim();
                var topic = string.IsNullOrEmpty(question) || IntakeContentGuard.IsUnsafe(question, requestText) ? gaps[i].Topic.Label() : $"{gaps[i].Topic.Label()}: {question}";
                topics.Add(topic.Length > MaxResearchTopicChars ? topic[..MaxResearchTopicChars].TrimEnd() : topic);
            }
        }

        return topics.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxResearchTopics).ToList();
    }
}
