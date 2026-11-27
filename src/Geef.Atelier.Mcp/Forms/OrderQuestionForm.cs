using System.Text.Json;
using System.Text.RegularExpressions;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using ModelContextProtocol.Protocol;

namespace Geef.Atelier.Mcp.Forms;

/// <summary>What came back from the form: how the person ended it and, when they sent it, the answers to pass to the order.</summary>
/// <param name="Outcome">How the form ended.</param>
/// <param name="Answers">The answers when <paramref name="Outcome"/> is <see cref="OrderFormOutcome.Accepted"/>, otherwise empty.</param>
internal sealed record OrderFormReading(OrderFormOutcome Outcome, IReadOnlyList<OrderAnswer> Answers);

/// <summary>How the form of <c>submit_order</c> ended.</summary>
internal enum OrderFormOutcome
{
    /// <summary>The person sent the form with at least one choice; every other question was left empty.</summary>
    Accepted = 0,

    /// <summary>The person declined to answer in the form.</summary>
    Declined = 1,

    /// <summary>The form was closed, nothing came back, or it was sent without a single choice.</summary>
    Cancelled = 2,

    /// <summary>Something came back that does not fit the form.</summary>
    Invalid = 3,
}

/// <summary>
/// The form of <c>submit_order</c> for the questions of an order in state <c>needs_input</c> (D-088): one choice per question with its answer options
/// and "You decide", and a choice for the revision guess. It has no field for typed text, so nothing can be entered into it; own words go through
/// the chat after the person closes the form. Nothing is required, so every question can be skipped. The message around it is fixed; questions,
/// options and the file name come from the order and must pass <see cref="OrderFormText"/>; forms are offered only for German and English orders.
/// </summary>
internal static partial class OrderQuestionForm
{
    /// <summary>Key of the form in <c>inputRequests</c> and <c>inputResponses</c>.</summary>
    public const string InputKey = "questions";

    /// <summary>Value of the option "You decide".</summary>
    public const string DecideForMeOption = "decide_for_me";

    /// <summary>Label of the option "You decide", as on the web page.</summary>
    public const string DecideForMeLabel = "You decide";

    /// <summary>Languages of the orders whose questions may be shown as a form.</summary>
    public static readonly IReadOnlySet<string> Languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "de", "en" };

    /// <summary>The fixed message of the form.</summary>
    public const string Message =
        "Atelier has a few short questions before it writes your text. Choose an answer or \"You decide\", or leave a question empty. " +
        "To answer in your own words, close this form and tell your assistant. " +
        "Atelier never asks for passwords, PINs, TANs, keys, card or account numbers.";

    /// <summary>Labels of the revision guess, as on the web page.</summary>
    public const string RevisionYesLabel = "Yes, improve it";

    /// <inheritdoc cref="RevisionYesLabel"/>
    public const string RevisionNoLabel = "No, write something new";

    /// <summary>The form for <paramref name="clarification"/>, or <c>null</c> with the reason when the questions cannot be shown as a form.</summary>
    public static (ElicitRequestParams? Form, string? Refused) Build(ClarificationView? clarification)
    {
        if (clarification is null)
            return (null, "no_clarification");
        if (clarification.Language is not { } language || !Languages.Contains(language.Trim()))
            return (null, "language");
        var questions = clarification.Questions ?? [];
        var revision = clarification.RevisionFileName;
        if (questions.Count == 0 && revision is null)
            return (null, "no_questions");
        if (questions.Count > 3)
            return (null, "too_many_questions");
        if (questions.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count() != questions.Count)
            return (null, "duplicate_question_id");

        var properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>(StringComparer.Ordinal);
        foreach (var question in questions)
        {
            if (Refusal(question) is { } refused)
                return (null, refused);
            properties[question.Id] = new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = question.Text.Trim(),
                OneOf =
                [
                    .. question.Options.Select(o => new ElicitRequestParams.EnumSchemaOption { Const = o.Id, Title = o.Label.Trim() }),
                    new ElicitRequestParams.EnumSchemaOption { Const = DecideForMeOption, Title = DecideForMeLabel },
                ],
            };
        }

        if (revision is not null)
        {
            if (!revision.IsSafeForForm(OrderFormText.MaxFileNameChars, fileName: true))
                return (null, "unsafe_file_name");
            properties[RevisionAnswer.QuestionId] = new ElicitRequestParams.TitledSingleSelectEnumSchema
            {
                Title = $"Improve the file “{revision.Trim()}”?",
                OneOf =
                [
                    new ElicitRequestParams.EnumSchemaOption { Const = RevisionAnswer.Yes, Title = RevisionYesLabel },
                    new ElicitRequestParams.EnumSchemaOption { Const = RevisionAnswer.No, Title = RevisionNoLabel },
                ],
            };
        }

        return (new ElicitRequestParams { Mode = "form", Message = Message, RequestedSchema = new() { Properties = properties } }, null);
    }

    /// <summary>
    /// Reads what came back for the form of <paramref name="clarification"/>. A sent form gives one answer per question, all with origin
    /// <see cref="AnswerOrigin.Form"/>: the chosen option, or "decide for me" for "You decide" and for an empty question; the revision guess only when
    /// it was chosen. A form sent without a single choice counts as closed (<see cref="OrderFormOutcome.Cancelled"/>), so a client that accepts forms
    /// by itself never starts the order for the person. Anything that does not fit the form (an unknown field such as typed text, an unknown option,
    /// a value that is no text) makes the whole form <see cref="OrderFormOutcome.Invalid"/>.
    /// </summary>
    public static OrderFormReading Read(ElicitResult? result, ClarificationView clarification)
    {
        ArgumentNullException.ThrowIfNull(clarification);
        if (result is null)
            return new(OrderFormOutcome.Cancelled, []);
        if (string.Equals(result.Action, "decline", StringComparison.OrdinalIgnoreCase))
            return new(OrderFormOutcome.Declined, []);
        if (!result.IsAccepted)
            return new(OrderFormOutcome.Cancelled, []);

        var content = result.Content ?? new Dictionary<string, JsonElement>();
        var questions = clarification.Questions ?? [];
        var known = new HashSet<string>(StringComparer.Ordinal);
        var answers = new List<OrderAnswer>();
        var chosen = 0;
        foreach (var question in questions)
        {
            known.Add(question.Id);
            if (!TryText(content, question.Id, out var choice))
                return Invalid;
            if (choice is not null)
                chosen++;

            if (choice is null or DecideForMeOption)
                answers.Add(new OrderAnswer(question.Id, null, null, true, AnswerOrigin.Form));
            else if (question.Options.Any(o => o.Id == choice))
                answers.Add(new OrderAnswer(question.Id, choice, null, false, AnswerOrigin.Form));
            else
                return Invalid;
        }

        if (clarification.RevisionFileName is not null)
        {
            known.Add(RevisionAnswer.QuestionId);
            if (!TryText(content, RevisionAnswer.QuestionId, out var revision) || revision is not (null or RevisionAnswer.Yes or RevisionAnswer.No))
                return Invalid;
            if (revision is not null)
            {
                chosen++;
                answers.Add(new OrderAnswer(RevisionAnswer.QuestionId, revision, null, false, AnswerOrigin.Form));
            }
        }

        if (!content.Keys.All(known.Contains))
            return Invalid;
        return chosen == 0 ? new(OrderFormOutcome.Cancelled, []) : new(OrderFormOutcome.Accepted, answers);
    }

    private static OrderFormReading Invalid => new(OrderFormOutcome.Invalid, []);

    private static string? Refusal(ClarifyQuestion question)
    {
        if (!IdPattern().IsMatch(question.Id ?? string.Empty) || question.Id == RevisionAnswer.QuestionId)
            return "unusable_question_id";
        if (!question.Text.IsSafeForForm(OrderFormText.MaxQuestionChars))
            return "unsafe_question";
        var options = question.Options ?? [];
        if (options.Count is < 1 or > 6)
            return "unusable_options";
        if (options.Select(o => o.Id).Distinct(StringComparer.Ordinal).Count() != options.Count)
            return "unusable_options";
        foreach (var option in options)
        {
            if (!OptionIdPattern().IsMatch(option.Id ?? string.Empty) || option.Id == DecideForMeOption)
                return "unusable_options";
            if (!option.Label.IsSafeForForm(OrderFormText.MaxOptionChars))
                return "unsafe_option";
        }

        return null;
    }

    private static bool TryText(IDictionary<string, JsonElement> content, string key, out string? text)
    {
        text = null;
        if (!content.TryGetValue(key, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return true;
        if (value.ValueKind != JsonValueKind.String)
            return false;
        var trimmed = value.GetString()!.Trim();
        text = trimmed.Length == 0 ? null : trimmed;
        return true;
    }

    [GeneratedRegex("^[a-z][a-z0-9]{0,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex OptionIdPattern();
}
