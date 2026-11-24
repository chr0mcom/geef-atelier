using System.Collections.Concurrent;
using System.Text;

namespace Geef.Atelier.Tests.EndToEnd.Fakes;

/// <summary>Thread-safe log of every LLM request the application sent during one end-to-end host lifetime.</summary>
internal sealed class LlmTrafficRecorder
{
    private readonly ConcurrentQueue<RecordedLlmCall> _calls = new();
    private int _index;

    /// <summary>Returns the next call index (1-based, in arrival order).</summary>
    public int NextIndex() => Interlocked.Increment(ref _index);

    /// <summary>Stores a recorded call.</summary>
    public void Add(RecordedLlmCall call) => _calls.Enqueue(call);

    /// <summary>All recorded calls in arrival order.</summary>
    public IReadOnlyList<RecordedLlmCall> Calls => _calls.OrderBy(c => c.Index).ToList();

    /// <summary>All recorded calls of one role in arrival order.</summary>
    public IReadOnlyList<RecordedLlmCall> CallsFor(LlmRole role) =>
        Calls.Where(c => c.Role == role).ToList();

    /// <summary>The full prompt text of every executor call.</summary>
    public IReadOnlyList<string> ExecutorPrompts() =>
        CallsFor(LlmRole.Executor).Select(c => c.PromptText).ToList();

    /// <summary>True when at least one executor prompt contains <paramref name="text"/> (ordinal comparison).</summary>
    public bool AnyExecutorPromptContains(string text) =>
        ExecutorPrompts().Any(p => p.Contains(text, StringComparison.Ordinal));

    /// <summary>A compact, human-readable summary of all calls for assertion messages.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        foreach (var call in Calls)
        {
            sb.Append('#').Append(call.Index).Append(' ').Append(call.Role)
              .Append(" model=").Append(call.Model ?? "-")
              .Append(" docMode=").Append(call.DocumentMode)
              .Append(" promptChars=").Append(call.PromptText.Length)
              .Append(" contextChars=").Append(call.ContextDocument?.Length ?? 0)
              .Append(" tools=").Append(string.Join(',', call.ToolNames))
              .AppendLine();
        }
        return sb.Length == 0 ? "(no LLM calls recorded)" : sb.ToString();
    }
}
