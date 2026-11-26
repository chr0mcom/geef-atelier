using System.Text.Json;
using System.Text.Json.Serialization;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>JSON form of <see cref="IntakeRecord"/> and of the answers (camelCase, as stored in the database).</summary>
public static class IntakeRecordJson
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes the record for <c>Orders.ClarifyJson</c> or <c>OrderIntakeShadows.RecordJson</c>.</summary>
    public static string Serialize(IntakeRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(record, JsonOptions);
    }

    /// <summary>Parses a stored record; null for null, empty or unreadable JSON.</summary>
    public static IntakeRecord? TryParse(string? json)
    {
        var record = Deserialize<IntakeRecord>(json);
        return record is null ? null : Normalize(record);
    }

    /// <summary>Serializes the answers for <c>Orders.AnswersJson</c>.</summary>
    public static string SerializeAnswers(IReadOnlyList<OrderAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        return JsonSerializer.Serialize(answers, JsonOptions);
    }

    /// <summary>Parses stored answers; an empty list for null, empty or unreadable JSON.</summary>
    public static IReadOnlyList<OrderAnswer> ParseAnswers(string? json)
    {
        var answers = Deserialize<List<OrderAnswer?>>(json);
        return answers is null
            ? []
            : answers.Where(a => a is not null && !string.IsNullOrWhiteSpace(a.QuestionId)).Select(a => a!).ToList();
    }

    private static T? Deserialize<T>(string? json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static IntakeRecord Normalize(IntakeRecord record) => record with
    {
        Output = record.Output is null ? null : Normalize(record.Output),
        Trace = record.Trace ?? [],
        Questions = (record.Questions ?? []).Where(q => q is not null).Select(q => q with { Options = q.Options ?? [] }).ToList(),
        Assumptions = (record.Assumptions ?? []).Where(a => a is not null).ToList(),
        FromMemory = record.FromMemory ?? [],
        ResearchTopics = record.ResearchTopics ?? [],
        HeldBack = record.HeldBack?.Where(q => !string.IsNullOrWhiteSpace(q)).ToList() is { Count: > 0 } held ? held : null,
    };

    private static ClarifierOutput Normalize(ClarifierOutput output) => output with
    {
        TextType = output.TextType ?? string.Empty,
        Language = output.Language ?? string.Empty,
        Understanding = output.Understanding ?? string.Empty,
        Gaps = (output.Gaps ?? []).Where(g => g is not null)
            .Select(g => g with { Question = g.Question is null ? null : g.Question with { Options = g.Question.Options ?? [] } })
            .ToList(),
        ResearchTopics = output.ResearchTopics ?? [],
    };
}
