using System.Globalization;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Application.Orders;

/// <summary>Checks the answers to the questions of an order and completes them; every problem is a plain-English <see cref="OrderAnswerException"/>.</summary>
internal static class OrderAnswerRules
{
    public const string RevisionId = "revision";
    public const string RevisionYes = "yes";
    public const string RevisionNo = "no";

    /// <summary>
    /// The answers in a fixed order: one per question (questions without an answer count as "decide for me"), then the corrections
    /// of assumptions, then the revision answer. Blank texts and option ids become null; nothing is remembered in stage 2.
    /// </summary>
    public static IReadOnlyList<OrderAnswer> Normalize(IntakeRecord? record, IReadOnlyList<OrderAnswer>? answers, int maxFreeTextChars)
    {
        var questions = record?.Questions ?? [];
        var assumptions = record?.Assumptions ?? [];
        var given = new Dictionary<string, OrderAnswer>(StringComparer.Ordinal);

        foreach (var raw in answers ?? [])
        {
            if (raw is null || string.IsNullOrWhiteSpace(raw.QuestionId))
                throw new OrderAnswerException("Every answer needs the id of its question.");

            var id = raw.QuestionId.Trim();
            if (given.ContainsKey(id))
                throw new OrderAnswerException($"The question \"{id}\" was answered more than once.");
            if (raw.Origin is not (AnswerOrigin.User or AnswerOrigin.Client))
                throw new OrderAnswerException($"The answer to \"{id}\" needs the origin \"user\" or \"client\".");

            var answer = raw with
            {
                QuestionId = id,
                OptionId = string.IsNullOrWhiteSpace(raw.OptionId) ? null : raw.OptionId.Trim(),
                FreeText = string.IsNullOrWhiteSpace(raw.FreeText) ? null : raw.FreeText.Trim(),
                Remember = RememberChoice.No,
            };
            if (answer.FreeText is { Length: var length } && length > maxFreeTextChars)
                throw new OrderAnswerException(string.Create(CultureInfo.InvariantCulture,
                    $"The answer to \"{id}\" is too long ({length} characters, at most {maxFreeTextChars})."));

            Check(answer, questions, assumptions, record?.RevisionSourceId is not null);
            given[id] = answer;
        }

        var origin = given.Count > 0 && given.Values.All(a => a.Origin == AnswerOrigin.Client) ? AnswerOrigin.Client : AnswerOrigin.User;
        var result = questions
            .Select(q => given.TryGetValue(q.Id, out var answer) ? answer : new OrderAnswer(q.Id, null, null, true, origin))
            .ToList();
        result.AddRange(assumptions.Where(a => given.ContainsKey(a.Id)).Select(a => given[a.Id]));
        if (given.TryGetValue(RevisionId, out var revision))
            result.Add(revision);
        return result;
    }

    private static void Check(OrderAnswer answer, IReadOnlyList<ClarifyQuestion> questions, IReadOnlyList<IntakeAssumption> assumptions, bool hasRevision)
    {
        var id = answer.QuestionId;
        if (questions.FirstOrDefault(q => q.Id == id) is { } question)
        {
            if (answer.OptionId is { } optionId && question.Options.All(o => o.Id != optionId))
                throw new OrderAnswerException($"\"{optionId}\" is not an answer option of question \"{id}\".");
            return;
        }

        if (assumptions.Any(a => a.Id == id))
        {
            if (answer.FreeText is null)
                throw new OrderAnswerException($"Please write the correction for \"{id}\" as free text.");
            return;
        }

        if (hasRevision && id == RevisionId)
        {
            if (answer.OptionId is not (RevisionYes or RevisionNo))
                throw new OrderAnswerException($"The answer to \"{RevisionId}\" must be \"{RevisionYes}\" or \"{RevisionNo}\".");
            return;
        }

        throw new OrderAnswerException($"There is no question \"{id}\" for this order.");
    }
}
