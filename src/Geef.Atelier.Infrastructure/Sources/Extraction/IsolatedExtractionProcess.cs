using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>How an isolated extraction ended.</summary>
internal enum IsolatedExtractionOutcome { Completed = 0, TimedOut = 1, MemoryExceeded = 2, OutputTooLarge = 3, Crashed = 4, Failed = 5, StartFailed = 6 }

/// <summary>Result of one isolated extraction with the measurements of the process.</summary>
internal sealed record IsolatedExtractionResult(SourceExtraction Extraction, IsolatedExtractionOutcome Outcome, int? ExitCode, long PeakMemoryBytes, TimeSpan Elapsed, int? ProcessId);

/// <summary>
/// Runs <see cref="SourceExtractionWorker"/> in a separate process of the same application: minimal environment without
/// the configuration of the web process, temporary working directory, managed heap limit
/// (<c>DOTNET_GCHeapHardLimit</c>), a memory watchdog, a bounded answer that is parsed while it streams in (no copy of
/// the whole answer) and a hard time limit after which the whole process tree is killed. Every failure becomes an unreadable result with a plain-English reason; nothing is thrown
/// for a broken or hostile file.
/// </summary>
internal sealed class IsolatedExtractionProcess(IOptions<SourceOptions> options, ILogger<IsolatedExtractionProcess> logger)
{
    internal const string TooComplexReason = "This file is too complex to read. Please save it again or split it into smaller files.";
    internal const string FailedReason = "This file could not be read.";
    internal const string UnavailableReason = "Files cannot be read right now. Please try again later.";
    internal const string TooSlowReason = "Reading this file took too long.";
    private const int ErrorCaptureChars = 256;
    private const long NativeMemoryAllowance = 268_435_456;
    private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ReapTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Called with the process id right after the process started (tests).</summary>
    internal Action<int>? ProcessStarted { get; init; }

    /// <summary>Lets the process accept the test modes of <see cref="SourceExtractionWorker"/> (tests).</summary>
    internal bool AllowTestModes { get; init; }

    /// <summary>Reads <paramref name="inputPath"/> as <paramref name="kind"/> in a separate process.</summary>
    public Task<IsolatedExtractionResult> RunAsync(SourceKind kind, string inputPath, string workDirectory, CancellationToken ct) =>
        RunAsync(kind.ToArgument(), inputPath, workDirectory, ct);

    internal async Task<IsolatedExtractionResult> RunAsync(string kindArgument, string inputPath, string workDirectory, CancellationToken ct)
    {
        var opts = options.Value;
        var clock = Stopwatch.StartNew();
        var start = BuildStart(opts, kindArgument, inputPath, workDirectory);
        if (start is null)
            return Finish(Unreadable(UnavailableReason), IsolatedExtractionOutcome.StartFailed, null, 0, clock, null);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                return Finish(Unreadable(UnavailableReason), IsolatedExtractionOutcome.StartFailed, null, 0, clock, null);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            logger.LogError("The extraction process could not be started: {Error}", ex.GetType().Name);
            return Finish(Unreadable(UnavailableReason), IsolatedExtractionOutcome.StartFailed, null, 0, clock, null);
        }

        var processId = process.Id;
        ProcessStarted?.Invoke(processId);
        process.StandardInput.Close();

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(opts.ExtractionTimeout);
        var watch = new ProcessWatch(process, opts.ExtractionMemoryLimitBytes + NativeMemoryAllowance);
        var answerStream = new BoundedReadStream(process.StandardOutput.BaseStream, opts.ExtractionMaxOutputBytes);
        var output = ParseAnswerAsync(answerStream, limit.Token);
        var errors = ReadErrorsAsync(process.StandardError, limit.Token);
        var watcher = watch.RunAsync(WatchInterval, limit.Token);

        var outcome = IsolatedExtractionOutcome.Completed;
        SourceExtraction? answer = null;
        try
        {
            answer = await output;
            if (answerStream.Exceeded)
                outcome = IsolatedExtractionOutcome.OutputTooLarge;
            else
                await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            outcome = ct.IsCancellationRequested ? IsolatedExtractionOutcome.Failed : IsolatedExtractionOutcome.TimedOut;
        }
        catch (IOException)
        {
            outcome = IsolatedExtractionOutcome.Failed;
        }

        if (watch.MemoryExceeded)
            outcome = IsolatedExtractionOutcome.MemoryExceeded;

        if (outcome != IsolatedExtractionOutcome.Completed || !process.HasExited)
            await KillAsync(process);

        await limit.CancelAsync();
        await Task.WhenAll(Quietly(watcher), Quietly(errors));
        var peak = watch.PeakBytes;
        int? exitCode = process.HasExited ? process.ExitCode : null;
        ct.ThrowIfCancellationRequested();

        if (watch.MemoryExceeded)
            outcome = IsolatedExtractionOutcome.MemoryExceeded;
        if (outcome != IsolatedExtractionOutcome.Completed)
            return Finish(Unreadable(ReasonFor(outcome)), outcome, exitCode, peak, clock, processId);

        return exitCode switch
        {
            SourceExtractionWorker.ExitOk => answer is { } extraction
                ? Finish(extraction, IsolatedExtractionOutcome.Completed, exitCode, peak, clock, processId)
                : Finish(Unreadable(FailedReason), IsolatedExtractionOutcome.Failed, exitCode, peak, clock, processId),
            SourceExtractionWorker.ExitTooComplex => Finish(Unreadable(TooComplexReason), IsolatedExtractionOutcome.MemoryExceeded, exitCode, peak, clock, processId),
            >= 128 => Finish(Unreadable(TooComplexReason), IsolatedExtractionOutcome.Crashed, exitCode, peak, clock, processId),
            _ => Finish(Unreadable(FailedReason), IsolatedExtractionOutcome.Failed, exitCode, peak, clock, processId),
        };
    }

    private ProcessStartInfo? BuildStart(SourceOptions opts, string kindArgument, string inputPath, string workDirectory)
    {
        var assembly = opts.ExtractionWorkerAssembly;
        if (string.IsNullOrWhiteSpace(assembly) || !File.Exists(assembly))
        {
            logger.LogError("The extraction process has no application assembly to run (Sources:ExtractionWorkerAssembly).");
            return null;
        }

        var dotnet = ResolveDotnet(opts.ExtractionDotnetPath);
        var start = new ProcessStartInfo(dotnet)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workDirectory,
        };
        start.ArgumentList.Add(assembly);
        start.ArgumentList.Add(SourceExtractionWorker.Switch);
        start.ArgumentList.Add(kindArgument);
        start.ArgumentList.Add(inputPath);

        var childTimeout = opts.ExtractionTimeout * 0.8;
        var environment = start.Environment;
        environment.Clear();
        environment["PATH"] = "/usr/local/bin:/usr/bin:/bin";
        environment["HOME"] = workDirectory;
        environment["TMPDIR"] = workDirectory;
        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_NOLOGO"] = "1";
        environment["DOTNET_gcServer"] = "0";
        environment["DOTNET_GCHeapHardLimit"] = "0x" + Math.Max(64L * 1_048_576, opts.ExtractionMemoryLimitBytes).ToString("X", CultureInfo.InvariantCulture);
        environment["DOTNET_PROCESSOR_COUNT"] = "2";
        environment[SourceExtractionWorker.LimitsVariable] = JsonSerializer.Serialize(ExtractionLimits.From(opts, childTimeout), ExtractionLimits.Json);
        if (AllowTestModes)
            environment[SourceExtractionWorker.TestModesVariable] = "1";
        return start;
    }

    internal static string ResolveDotnet(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;
        var current = Environment.ProcessPath;
        if (current is not null && Path.GetFileNameWithoutExtension(current).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            return current;
        var fromRuntime = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        return File.Exists(fromRuntime) ? fromRuntime : "dotnet";
    }

    private static async Task<SourceExtraction?> ParseAnswerAsync(BoundedReadStream stream, CancellationToken ct)
    {
        SourceExtraction? extraction;
        try
        {
            extraction = await JsonSerializer.DeserializeAsync<SourceExtraction>(stream, ExtractionLimits.Json, ct);
        }
        catch (JsonException)
        {
            await stream.CopyToAsync(Stream.Null, ct);
            return null;
        }

        return extraction is null || stream.Exceeded ? null : Validate(extraction);
    }

    private static async Task<string> ReadErrorsAsync(StreamReader reader, CancellationToken ct)
    {
        var kept = new char[ErrorCaptureChars];
        var keptLength = 0;
        var chunk = new char[4_096];
        int read;
        while ((read = await reader.ReadAsync(chunk.AsMemory(), ct)) > 0)
        {
            var take = Math.Min(read, kept.Length - keptLength);
            chunk.AsSpan(0, take).CopyTo(kept.AsSpan(keptLength));
            keptLength += take;
        }

        return new string(kept, 0, keptLength);
    }

    private async Task KillAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            logger.LogWarning("The extraction process could not be stopped: {Error}", ex.GetType().Name);
        }

        using var reap = new CancellationTokenSource(ReapTimeout);
        try
        {
            await process.WaitForExitAsync(reap.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogError("The extraction process did not end after it was stopped.");
        }
    }

    private static SourceExtraction? Validate(SourceExtraction extraction)
    {
        if (!Enum.IsDefined(extraction.Quality))
            return null;
        if (extraction.Quality == SourceQuality.Unreadable)
            return string.IsNullOrWhiteSpace(extraction.FailureReason) ? extraction with { FailureReason = FailedReason } : extraction;
        return extraction.Text is null ? null : extraction with { EmptyPages = extraction.EmptyPages ?? [], Warnings = extraction.Warnings ?? [] };
    }

    private static string ReasonFor(IsolatedExtractionOutcome outcome) => outcome switch
    {
        IsolatedExtractionOutcome.TimedOut => TooSlowReason,
        IsolatedExtractionOutcome.MemoryExceeded or IsolatedExtractionOutcome.OutputTooLarge or IsolatedExtractionOutcome.Crashed => TooComplexReason,
        _ => FailedReason,
    };

    private static SourceExtraction Unreadable(string reason) => SourceExtraction.Unreadable(reason);

    private static IsolatedExtractionResult Finish(SourceExtraction extraction, IsolatedExtractionOutcome outcome, int? exitCode, long peak, Stopwatch clock, int? processId) =>
        new(extraction, outcome, exitCode, peak, clock.Elapsed, processId);

    private static async Task Quietly(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or InvalidOperationException)
        {
        }
    }

    /// <summary>Reads at most a number of bytes from the answer of the process; beyond it the stream ends and <see cref="Exceeded"/> is set.</summary>
    private sealed class BoundedReadStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;

        public bool Exceeded { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (Exceeded)
                return 0;
            var read = inner.Read(buffer);
            return Count(read);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Exceeded)
                return 0;
            var read = await inner.ReadAsync(buffer, cancellationToken);
            return Count(read);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int Count(int read)
        {
            _read += read;
            if (_read <= maxBytes)
                return read;
            Exceeded = true;
            return 0;
        }
    }

    private sealed class ProcessWatch(Process process, long killAboveBytes)
    {
        private long _peak;

        public long PeakBytes => Interlocked.Read(ref _peak);

        public bool MemoryExceeded { get; private set; }

        public async Task RunAsync(TimeSpan interval, CancellationToken ct)
        {
            using var timer = new PeriodicTimer(interval);
            do
            {
                if (!Sample())
                    return;
            }
            while (await timer.WaitForNextTickAsync(ct));
        }

        private bool Sample()
        {
            try
            {
                if (process.HasExited)
                    return false;
                process.Refresh();
                var current = Math.Max(process.WorkingSet64, process.PeakWorkingSet64);
                if (current > Interlocked.Read(ref _peak))
                    Interlocked.Exchange(ref _peak, current);
                if (current <= killAboveBytes)
                    return true;
                MemoryExceeded = true;
                process.Kill(entireProcessTree: true);
                return false;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                return false;
            }
        }
    }
}
