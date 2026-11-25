using System.Diagnostics;

namespace Geef.Atelier.Tools.FactCheckEval;

/// <summary>Decides before every model call whether the shared Codex subscription may be used now.</summary>
public interface ICodexGuard
{
    /// <summary>Why no call may start now (a lock file exists or a Codex process runs), or null when it may.</summary>
    string? Blocker();
}

/// <summary>
/// The guard of contract stage 3, section 3: no call while one of <paramref name="lockFiles"/> exists or a Codex job runs, that
/// is a process whose name contains <c>codex</c> with the <c>exec</c> subcommand (<c>pgrep -a codex</c>). An idle Codex server of
/// an editor extension uses no quota and does not block. The lock files are parameters, never fixed paths. When the process list
/// cannot be read, the guard blocks (fail closed).
/// </summary>
public sealed class CodexGuard(IReadOnlyList<string> lockFiles, Func<string?> codexProcesses) : ICodexGuard
{
    /// <summary>A guard over the real file system and <c>pgrep</c>.</summary>
    public static CodexGuard ForHost(IReadOnlyList<string> lockFiles) => new(lockFiles, Pgrep);

    /// <inheritdoc/>
    public string? Blocker()
    {
        foreach (var file in lockFiles)
        {
            if (File.Exists(file))
                return $"lock file {file} exists";
        }
        var processes = codexProcesses();
        if (processes is null)
            return "the process list could not be read (pgrep failed)";
        var jobs = processes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Count(IsJob);
        return jobs > 0 ? $"a Codex process runs ({jobs} job(s) with the exec subcommand found by pgrep -a codex)" : null;
    }

    private static bool IsJob(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Contains("exec");

    /// <summary>Output of <c>pgrep -a codex</c>: empty when no such process runs, null when pgrep could not answer.</summary>
    private static string? Pgrep()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("pgrep", ["-a", "codex"])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
                return null;
            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000))
                return null;
            return process.ExitCode switch
            {
                0 => output,
                1 => string.Empty,
                _ => null,
            };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// The call cap of one invocation: at most <paramref name="maxCalls"/> attempts, and never more than <paramref name="ceiling"/>
/// attempts in the result file altogether (<paramref name="alreadyUsed"/> lines are in it). Every attempt counts, also a failed one.
/// </summary>
public sealed class CallBudget(int maxCalls, int ceiling, int alreadyUsed)
{
    /// <summary>Attempts made by this invocation.</summary>
    public int Used { get; private set; }

    /// <summary>Attempts this invocation may still make.</summary>
    public int Remaining => Math.Max(0, Math.Min(maxCalls, ceiling - alreadyUsed) - Used);

    /// <summary>Takes one attempt; false when the cap is reached.</summary>
    public bool TryTake()
    {
        if (Remaining == 0)
            return false;
        Used++;
        return true;
    }
}
