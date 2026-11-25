namespace Geef.Atelier.Tests.EndToEnd.Fakes;

/// <summary>One executor call that stays open, so a test can look at the order while its text is being written.</summary>
internal sealed class ExecutorHold
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _abandoned = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<(string? Text, int Status, string? Code)> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the application has sent the held call.</summary>
    public Task Reached => _reached.Task;

    /// <summary>Completes when the application cancelled the held call before it was answered.</summary>
    public Task Abandoned => _abandoned.Task;

    /// <summary>Answers the held call with <paramref name="text"/>.</summary>
    public void Answer(string text) => _answer.TrySetResult((text, 200, null));

    /// <summary>Answers the held call with the cli-proxy error envelope with <paramref name="code"/>.</summary>
    public void Fail(int status, string code) => _answer.TrySetResult((null, status, code));

    /// <summary>Marks the call as reached and waits for the answer of the test or the cancellation of the caller.</summary>
    public async Task<(string? Text, int Status, string? Code)> WaitAsync(CancellationToken ct)
    {
        _reached.TrySetResult();
        try
        {
            return await _answer.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            _abandoned.TrySetResult();
            throw;
        }
    }
}
