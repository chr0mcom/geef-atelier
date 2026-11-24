using System.Text;

namespace Geef.Atelier.Tests.EndToEnd.Fakes;

/// <summary>Role of an outgoing LLM call, derived from the request body.</summary>
internal enum LlmRole
{
    Executor,
    Reviewer,
    Refiner,
    QueryExtractor,
    Other,
}

/// <summary>One chat message of a recorded LLM request.</summary>
internal sealed record RecordedMessage(string Role, string? Content);

/// <summary>
/// One recorded request to the OpenAI-compatible chat endpoint, as the Atelier sent it to the cli-proxy.
/// </summary>
internal sealed record RecordedLlmCall(
    int Index,
    string Method,
    string Url,
    string? Model,
    LlmRole Role,
    IReadOnlyList<RecordedMessage> Messages,
    bool DocumentMode,
    string? Document,
    string? ContextDocument,
    IReadOnlyList<string> ToolNames,
    string? ToolChoice,
    bool HasSynthesizeSchema,
    string RawBody)
{
    /// <summary>Content of the first system message, or an empty string.</summary>
    public string SystemPrompt =>
        Messages.FirstOrDefault(m => m.Role == "system")?.Content ?? string.Empty;

    /// <summary>
    /// Everything the model would see: all message contents plus the document-mode fields
    /// (<c>draft.md</c> content and the <c>context.md</c> context document).
    /// </summary>
    public string PromptText
    {
        get
        {
            var sb = new StringBuilder();
            foreach (var message in Messages)
                sb.AppendLine(message.Content);
            if (Document is not null)
                sb.AppendLine(Document);
            if (ContextDocument is not null)
                sb.AppendLine(ContextDocument);
            return sb.ToString();
        }
    }
}
