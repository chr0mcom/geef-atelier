using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Application.Orders;

/// <summary>Which questions of the intake the person left open: skipped, not answered in time, or left to Atelier with "You decide".</summary>
public static class OpenQuestions
{
    /// <summary>
    /// True when <paramref name="question"/> was asked by the clarifier and has no answer of the person: no answer at all (skipped,
    /// started without answers, not answered in time), "decide for me", or an answer that names neither a known option nor own words.
    /// The fixed length question (<see cref="ClarifyQuestion.FromTemplate"/>) never commits the person and is never open.
    /// </summary>
    public static bool IsLeftOpen(this ClarifyQuestion question, IReadOnlyDictionary<string, OrderAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(answers);
        if (question.FromTemplate)
            return false;
        if (!answers.TryGetValue(question.Id, out var answer))
            return true;
        if (answer.DecideForMe)
            return true;
        var knownOption = answer.OptionId is { } optionId && question.Options.Any(o => o.Id == optionId);
        return !knownOption && string.IsNullOrWhiteSpace(answer.FreeText);
    }
}
