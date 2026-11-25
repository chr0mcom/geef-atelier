namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>
/// Finds calendar elements in a text without an AI model: dates written with digits (<c>12.5.</c>, <c>2027-05-12</c>), month names next
/// to a digit (<c>5. Mai</c>, <c>May 5</c>, <c>Mai 2027</c>; a month name alone never counts) and weekday names as whole words. The names
/// come from the calendars of German, English, French, Spanish, Italian and the language of the order, never from a kept word list.
/// </summary>
public static class CalendarMentions
{
    /// <summary>
    /// The calendar elements of <paramref name="text"/>, normalized so that the same date written in the request or a file compares equal
    /// (digits without leading zero); <paramref name="language"/> is the language of the order and may be <c>null</c>.
    /// </summary>
    public static IReadOnlyList<string> Find(string text, string? language) => throw new NotImplementedException();
}
