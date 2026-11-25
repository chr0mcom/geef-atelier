using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>Turns intake answers into checks without a text parser.</summary>
public static class IntakeConstraints
{
    /// <summary>Adds the answered length to <paramref name="fromRequest"/> when the request itself has none; everything else unchanged.</summary>
    public static OrderConstraints WithAnswers(OrderConstraints fromRequest, IntakeRecord? record, IReadOnlyList<OrderAnswer> answers)
    {
        if (fromRequest.Length is not null || record?.Output is not { } output)
            return fromRequest;
        if (!record.Questions.Any(q => q.Id == QuestionTemplates.LengthQuestionId && q.FromTemplate))
            return fromRequest;

        var answer = answers.LastOrDefault(a => a.QuestionId == QuestionTemplates.LengthQuestionId);
        var length = answer is null ? null : QuestionTemplates.ToLengthConstraint(answer, output.Language, output.LengthScale);
        return length is null ? fromRequest : fromRequest with { Length = length };
    }
}
