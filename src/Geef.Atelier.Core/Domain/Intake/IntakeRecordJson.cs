namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>JSON form of <see cref="IntakeRecord"/> and of the answers (camelCase, as stored in the database).</summary>
public static class IntakeRecordJson
{
    /// <summary>Serializes the record for <c>Orders.ClarifyJson</c> or <c>OrderIntakeShadows.RecordJson</c>.</summary>
    public static string Serialize(IntakeRecord record) => throw new NotImplementedException();

    /// <summary>Parses a stored record; null for null, empty or unreadable JSON.</summary>
    public static IntakeRecord? TryParse(string? json) => throw new NotImplementedException();

    /// <summary>Serializes the answers for <c>Orders.AnswersJson</c>.</summary>
    public static string SerializeAnswers(IReadOnlyList<OrderAnswer> answers) => throw new NotImplementedException();

    /// <summary>Parses stored answers; an empty list for null, empty or unreadable JSON.</summary>
    public static IReadOnlyList<OrderAnswer> ParseAnswers(string? json) => throw new NotImplementedException();
}
