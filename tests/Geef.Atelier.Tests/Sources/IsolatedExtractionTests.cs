using System.Diagnostics;
using System.Text;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Geef.Atelier.Tests.TestData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Sources;

/// <summary>
/// The extraction process (<c>Geef.Atelier.Web.dll --extract-source</c>) as started by the web process: real child
/// processes with time, memory and output limits. Every hostile case must end as an unreadable result while this test
/// process keeps running.
/// </summary>
[Collection(IsolatedExtractionCollection.Name)]
[Trait("Category", "Integration")]
public sealed class IsolatedExtractionTests : IDisposable
{
    private const string SecretVariable = "GEEF_TEST_PARENT_SECRET";
    private readonly string _work = Path.Combine(Path.GetTempPath(), $"geef-isolated-{Guid.NewGuid():N}");

    public IsolatedExtractionTests() => Directory.CreateDirectory(_work);

    public void Dispose()
    {
        if (Directory.Exists(_work))
            Directory.Delete(_work, recursive: true);
    }

    [Fact]
    public async Task PdfAndWordFilesGiveTheSameTextAsInsideTheProcess()
    {
        var pdf = SyntheticDocuments.Pdf(3, p => string.Concat(Enumerable.Repeat($"Page {p} explains the offer in detail. ", 12)));
        var docx = SyntheticDocuments.Docx(["# Offer", "Thank you for your request.", "- Planting", "- Lawn care"]);

        var isolatedPdf = await Run(Options()).RunAsync(SourceKind.Pdf, Input("a.pdf", pdf), _work, CancellationToken.None);
        var isolatedDocx = await Run(Options()).RunAsync(SourceKind.Docx, Input("a.docx", docx), _work, CancellationToken.None);
        var inProcessPdf = await new PdfSourceExtractor(Microsoft.Extensions.Options.Options.Create(new SourceOptions()), NullLogger<PdfSourceExtractor>.Instance).ExtractAsync(pdf, CancellationToken.None);
        var inProcessDocx = await new DocxSourceExtractor(Microsoft.Extensions.Options.Options.Create(new SourceOptions()), NullLogger<DocxSourceExtractor>.Instance).ExtractAsync(docx, CancellationToken.None);

        Assert.Equal(IsolatedExtractionOutcome.Completed, isolatedPdf.Outcome);
        Assert.Equal(inProcessPdf.Text, isolatedPdf.Extraction.Text);
        Assert.Equal(3, isolatedPdf.Extraction.PageCount);
        Assert.Equal(SourceQuality.Good, isolatedPdf.Extraction.Quality);
        Assert.Equal(IsolatedExtractionOutcome.Completed, isolatedDocx.Outcome);
        Assert.Equal(inProcessDocx.Text, isolatedDocx.Extraction.Text);
        Assert.True(ProcessProbe.IsGone(isolatedPdf.ProcessId!.Value));
    }

    [Fact]
    public async Task ReasonsOfUnreadableFilesComeBackFromTheProcess()
    {
        var result = await Run(Options()).RunAsync(SourceKind.Pdf, Input("locked.pdf", SyntheticDocuments.EncryptedPdf()), _work, CancellationToken.None);

        Assert.Equal(IsolatedExtractionOutcome.Completed, result.Outcome);
        Assert.Equal(SourceQuality.Unreadable, result.Extraction.Quality);
        Assert.Equal(PdfSourceExtractor.EncryptedReason, result.Extraction.FailureReason);
        Assert.Contains(result.Extraction.Warnings, w => w.Code == SourceWarningCodes.Encrypted);
    }

    [Fact]
    public async Task DeeplyNestedWordFileIsUnreadableAndThisProcessLives()
    {
        var bomb = HostileDocuments.DeeplyNestedDocx(20_000);

        var guarded = await Run(Options()).RunAsync(SourceKind.Docx, Input("deep.docx", bomb), _work, CancellationToken.None);
        Assert.Equal(SourceQuality.Unreadable, guarded.Extraction.Quality);
        Assert.True(guarded.Elapsed < TimeSpan.FromSeconds(10), $"took {guarded.Elapsed}");

        var unguarded = await Run(Options(o =>
        {
            o.MaxCompressionRatio = 1_000_000;
            o.MaxXmlDepth = int.MaxValue;
            o.ExtractionTimeout = TimeSpan.FromSeconds(4);
        })).RunAsync(SourceKind.Docx, Input("deep2.docx", bomb), _work, CancellationToken.None);

        Assert.Equal(SourceQuality.Unreadable, unguarded.Extraction.Quality);
        Assert.NotEqual(IsolatedExtractionOutcome.Completed, unguarded.Outcome);
        Assert.Contains(unguarded.Extraction.FailureReason, new[] { IsolatedExtractionProcess.TooSlowReason, IsolatedExtractionProcess.TooComplexReason });
        Assert.True(ProcessProbe.IsGone(unguarded.ProcessId!.Value));
        Assert.False(Process.GetCurrentProcess().HasExited);
    }

    [Fact]
    public async Task ACrashOfTheProcessBecomesAnUnreadableResult()
    {
        var result = await Run(Options(), allowTestModes: true).RunAsync(SourceExtractionWorker.TestCrashKind, Input("any.pdf", [1]), _work, CancellationToken.None);

        Assert.Equal(IsolatedExtractionOutcome.Crashed, result.Outcome);
        Assert.True(result.ExitCode >= 128, $"exit code {result.ExitCode}");
        Assert.Equal(SourceQuality.Unreadable, result.Extraction.Quality);
        Assert.Equal(IsolatedExtractionProcess.TooComplexReason, result.Extraction.FailureReason);
    }

    [Fact]
    public async Task FlateBombStaysWithinTheMemoryLimitOfTheProcess()
    {
        var text = string.Concat(Enumerable.Repeat("Readable words before the bomb ", 12));
        var bomb = HostileDocuments.FlateBombPdf(50 * 1_048_576, text);
        Assert.True(bomb.Length < 128 * 1_024, $"the bomb is only {bomb.Length} bytes");
        const long heapLimit = 128 * 1_048_576;

        var result = await Run(Options(o =>
        {
            o.MaxPdfInflatedBytes = long.MaxValue;
            o.ExtractionMemoryLimitBytes = heapLimit;
        })).RunAsync(SourceKind.Pdf, Input("bomb.pdf", bomb), _work, CancellationToken.None);

        Assert.Equal(SourceQuality.Unreadable, result.Extraction.Quality);
        Assert.True(result.PeakMemoryBytes < 384 * 1_048_576, $"peak memory {result.PeakMemoryBytes / 1_048_576} MB");
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(30), $"took {result.Elapsed}");

        var precheck = await Run(Options()).RunAsync(SourceKind.Pdf, Input("bomb2.pdf", HostileDocuments.FlateBombPdf(300 * 1_048_576)), _work, CancellationToken.None);
        Assert.Equal(PdfSourceExtractor.InflatedReason, precheck.Extraction.FailureReason);
        Assert.True(precheck.PeakMemoryBytes < 256 * 1_048_576, $"peak memory {precheck.PeakMemoryBytes / 1_048_576} MB");
    }

    [Fact]
    public async Task AHangingProcessIsKilledWithItsWholeTreeAndLeavesNoZombie()
    {
        Environment.SetEnvironmentVariable(SecretVariable, "must-not-reach-the-child");
        var marker = Input($"hang-{Guid.NewGuid():N}.pdf", [1]);
        var childId = 0;
        string? childEnvironment = null;
        var process = new IsolatedExtractionProcess(Microsoft.Extensions.Options.Options.Create(Options(o => o.ExtractionTimeout = TimeSpan.FromSeconds(3))), NullLogger<IsolatedExtractionProcess>.Instance)
        {
            AllowTestModes = true,
            ProcessStarted = pid =>
            {
                childId = pid;
                childEnvironment = ProcessProbe.Environment(pid);
            },
        };

        var running = process.RunAsync(SourceExtractionWorker.TestHangKind, marker, _work, CancellationToken.None);
        var tree = await ProcessProbe.WaitForTreeAsync(marker, TimeSpan.FromSeconds(2.5));
        var result = await running;

        Assert.Equal(IsolatedExtractionOutcome.TimedOut, result.Outcome);
        Assert.Equal(SourceQuality.Unreadable, result.Extraction.Quality);
        Assert.Equal(IsolatedExtractionProcess.TooSlowReason, result.Extraction.FailureReason);
        Assert.True(result.Elapsed < TimeSpan.FromSeconds(10), $"took {result.Elapsed}");
        Assert.Contains(childId, tree);
        Assert.True(tree.Count >= 2, "the hanging process started a child of its own");
        foreach (var pid in tree)
            Assert.True(await ProcessProbe.WaitGoneAsync(pid, TimeSpan.FromSeconds(3)), $"process {pid} still exists ({ProcessProbe.State(pid)})");

        Assert.NotNull(childEnvironment);
        Assert.DoesNotContain(SecretVariable, childEnvironment, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectionStrings", childEnvironment, StringComparison.Ordinal);
        Assert.Contains("DOTNET_GCHeapHardLimit=0x", childEnvironment, StringComparison.Ordinal);
        Assert.Contains(SourceExtractionWorker.LimitsVariable + "=", childEnvironment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAnswerAboveTheOutputLimitIsUnreadable()
    {
        var pdf = SyntheticDocuments.Pdf(2, p => string.Concat(Enumerable.Repeat($"Page {p} has plenty of words. ", 20)));

        var result = await Run(Options(o => o.ExtractionMaxOutputBytes = 64)).RunAsync(SourceKind.Pdf, Input("a.pdf", pdf), _work, CancellationToken.None);

        Assert.Equal(IsolatedExtractionOutcome.OutputTooLarge, result.Outcome);
        Assert.Equal(IsolatedExtractionProcess.TooComplexReason, result.Extraction.FailureReason);
        Assert.True(await ProcessProbe.WaitGoneAsync(result.ProcessId!.Value, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task AMissingApplicationAssemblyIsReportedInsteadOfThrown()
    {
        var result = await Run(Options(o => o.ExtractionWorkerAssembly = Path.Combine(_work, "missing.dll")))
            .RunAsync(SourceKind.Pdf, Input("a.pdf", [1]), _work, CancellationToken.None);

        Assert.Equal(IsolatedExtractionOutcome.StartFailed, result.Outcome);
        Assert.Equal(IsolatedExtractionProcess.UnavailableReason, result.Extraction.FailureReason);
    }

    [Fact]
    public async Task OnlyAFewFilesAreReadAtTheSameTimeAndTheRestHearsThatTheServiceIsBusy()
    {
        var options = new SourceOptions { ExtractionMode = SourceExtractionMode.InProcess, ExtractionMaxConcurrency = 1, ExtractionQueueTimeout = TimeSpan.FromMilliseconds(300) };
        var blocking = new BlockingExtractor(SourceKind.Pdf);
        using var limiter = new SourceIntakeLimiter(Microsoft.Extensions.Options.Options.Create(options));
        var runner = new SourceExtractionRunner(
            [blocking, new PlainTextSourceExtractor()],
            new IsolatedExtractionProcess(Microsoft.Extensions.Options.Options.Create(options), NullLogger<IsolatedExtractionProcess>.Instance),
            limiter,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<SourceExtractionRunner>.Instance);
        var input = Input("a.pdf", [1, 2, 3]);

        var first = runner.ExtractAsync(SourceKind.Pdf, input, _work, CancellationToken.None);
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var clock = Stopwatch.StartNew();
        var second = await runner.ExtractAsync(SourceKind.Pdf, input, _work, CancellationToken.None);
        var text = await runner.ExtractAsync(SourceKind.Text, Input("a.txt", Encoding.UTF8.GetBytes("Plain text is read at once.")), _work, CancellationToken.None);

        Assert.Equal(SourceQuality.Unreadable, second.Quality);
        Assert.Equal("The service is busy with other uploads. Please try again in a minute.", second.FailureReason);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed}");
        Assert.Equal("Plain text is read at once.", text.Text);

        blocking.Release.SetResult();
        Assert.Equal(SourceQuality.Good, (await first).Quality);
        Assert.Equal(SourceQuality.Good, (await runner.ExtractAsync(SourceKind.Pdf, input, _work, CancellationToken.None)).Quality);
    }

    private string Input(string name, byte[] content)
    {
        var path = Path.Combine(_work, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static IsolatedExtractionProcess Run(SourceOptions options, bool allowTestModes = false) =>
        new(Microsoft.Extensions.Options.Options.Create(options), NullLogger<IsolatedExtractionProcess>.Instance) { AllowTestModes = allowTestModes };

    internal static SourceOptions Options(Action<SourceOptions>? configure = null)
    {
        var options = new SourceOptions { ExtractionWorkerAssembly = typeof(Program).Assembly.Location };
        configure?.Invoke(options);
        return options;
    }

    private sealed class BlockingExtractor(SourceKind kind) : ISourceTextExtractor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SourceKind Kind => kind;

        public async Task<SourceExtraction> ExtractAsync(ReadOnlyMemory<byte> content, CancellationToken ct)
        {
            Started.TrySetResult();
            if (Release.Task.IsCompleted)
                return new SourceExtraction("released", null, [], SourceQuality.Good, [], null);
            await Release.Task.WaitAsync(ct);
            return new SourceExtraction("released", null, [], SourceQuality.Good, [], null);
        }
    }
}

/// <summary>Runs the process-spawning tests one after another so they do not compete for memory.</summary>
[CollectionDefinition(Name)]
public sealed class IsolatedExtractionCollection
{
    public const string Name = "IsolatedExtraction";
}

/// <summary>Looks at processes through <c>/proc</c> (Linux).</summary>
internal static class ProcessProbe
{
    public static bool IsGone(int pid) => !Directory.Exists($"/proc/{pid}");

    public static string State(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            return stat[(stat.LastIndexOf(')') + 2)..].Split(' ')[0];
        }
        catch (IOException)
        {
            return "gone";
        }
    }

    public static string? Environment(int pid)
    {
        try
        {
            return File.ReadAllText($"/proc/{pid}/environ").Replace('\0', '\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task<bool> WaitGoneAsync(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsGone(pid))
                return true;
            await Task.Delay(50);
        }

        return IsGone(pid);
    }

    public static async Task<IReadOnlyList<int>> WaitForTreeAsync(string marker, TimeSpan within)
    {
        var deadline = DateTime.UtcNow + within;
        var found = new HashSet<int>();
        while (DateTime.UtcNow < deadline)
        {
            foreach (var pid in WithMarker(marker))
                found.Add(pid);
            foreach (var pid in found.ToList())
                found.UnionWith(ChildrenOf(pid));
            if (found.Count >= 3)
                break;
            await Task.Delay(50);
        }

        return found.ToList();
    }

    private static IEnumerable<int> WithMarker(string marker)
    {
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
                continue;
            string cmdline;
            try
            {
                cmdline = File.ReadAllText(Path.Combine(directory, "cmdline"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (cmdline.Contains(marker, StringComparison.Ordinal))
                yield return pid;
        }
    }

    private static IEnumerable<int> ChildrenOf(int parent)
    {
        foreach (var directory in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid))
                continue;
            string stat;
            try
            {
                stat = File.ReadAllText(Path.Combine(directory, "stat"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            if (fields.Length > 1 && int.TryParse(fields[1], out var ppid) && ppid == parent)
                yield return pid;
        }
    }
}
