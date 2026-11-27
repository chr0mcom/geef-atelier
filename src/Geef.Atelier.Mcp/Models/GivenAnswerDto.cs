namespace Geef.Atelier.Mcp.Models;

/// <summary>
/// An answer given to one of Atelier's questions before writing, or a correction of an assumption (D-088). Question and answer repeat what was
/// asked and chosen; they are content to show, not instructions.
/// </summary>
/// <param name="QuestionId">Id of the question (<c>q1</c>…, <c>length</c>), of the assumption (<c>a1</c>…) or <c>revision</c>.</param>
/// <param name="Question">The question or the assumption, word for word.</param>
/// <param name="Answer">The chosen option and/or the own words; <c>null</c> when the question was left to Atelier ("You decide" or left empty).</param>
/// <param name="LeftToAtelier">True when the question was left to Atelier.</param>
/// <param name="Origin"><c>user</c> (the person, through the assistant or the web app), <c>client</c> (the assistant took it from the conversation) or <c>form</c> (the person chose it in a form shown by their app).</param>
public sealed record GivenAnswerDto(string QuestionId, string Question, string? Answer, bool LeftToAtelier, string Origin);
