using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>
/// Extraction mode of the application process: <c>dotnet Geef.Atelier.Web.dll --extract-source &lt;pdf|docx&gt; &lt;file&gt;</c>.
/// Runs before any host is built (no DI, database, configuration or sign-in), reads the limits from
/// <see cref="LimitsVariable"/>, writes the <see cref="SourceExtraction"/> as JSON to standard output and, on failure,
/// a short error code to standard error with a matching exit code. The modes <see cref="TestHangKind"/>,
/// <see cref="TestCrashKind"/>, <see cref="TestManagedMemoryKind"/> and <see cref="TestNativeMemoryKind"/> exist for tests
/// and are accepted only when <see cref="TestModesVariable"/> is set.
/// </summary>
internal static class SourceExtractionWorker
{
    public const string Switch = "--extract-source";
    public const string LimitsVariable = "GEEF_EXTRACTION_LIMITS";
    public const string TestModesVariable = "GEEF_EXTRACTION_TEST_MODES";
    public const string TestHangKind = "test-hang";
    public const string TestCrashKind = "test-crash";
    public const string TestManagedMemoryKind = "test-managed-memory";
    public const string TestNativeMemoryKind = "test-native-memory";
    private const long TestNativeMemoryCeiling = 900L * 1_048_576;
    public const int ExitOk = 0;
    public const int ExitUsage = 2;
    public const int ExitUnreadable = 3;
    public const int ExitTooComplex = 4;

    /// <summary>True when <paramref name="args"/> ask for the extraction mode.</summary>
    public static bool IsRequested(string[] args) => args is [Switch, ..];

    /// <summary>Runs one extraction and returns the exit code.</summary>
    public static async Task<int> RunAsync(string[] args, Stream output, TextWriter errors)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(errors);

        if (args is not [Switch, var kindArgument, var path])
            return Fail(errors, ExitUsage, "usage");
        var testModes = Environment.GetEnvironmentVariable(TestModesVariable) == "1";
        if (testModes)
        {
            if (kindArgument == TestHangKind)
                return Hang(path);
            if (kindArgument == TestCrashKind)
                return Crash(0);
            if (kindArgument == TestNativeMemoryKind)
                return GrowNativeMemory();
        }

        if (ParseKind(kindArgument) is null && !(testModes && kindArgument == TestManagedMemoryKind))
            return Fail(errors, ExitUsage, "usage");

        try
        {
            if (kindArgument == TestManagedMemoryKind)
                return GrowManagedMemory();

            var options = ReadLimits()?.ToOptions() ?? new SourceOptions();
            var bytes = await File.ReadAllBytesAsync(path);
            ISourceTextExtractor extractor = ParseKind(kindArgument) == SourceKind.Pdf
                ? new PdfSourceExtractor(Options.Create(options), NullLogger<PdfSourceExtractor>.Instance)
                : new DocxSourceExtractor(Options.Create(options), NullLogger<DocxSourceExtractor>.Instance);
            var result = await extractor.ExtractAsync(bytes, CancellationToken.None);
            await JsonSerializer.SerializeAsync(output, result, ExtractionLimits.Json);
            await output.FlushAsync();
            return ExitOk;
        }
        catch (Exception ex) when (ex is OutOfMemoryException or InsufficientExecutionStackException)
        {
            return Fail(errors, ExitTooComplex, "too_complex");
        }
        catch (Exception)
        {
            return Fail(errors, ExitUnreadable, "unreadable");
        }
    }

    /// <summary>The command line argument for <paramref name="kind"/>; only PDF and Word files are read in the extraction process.</summary>
    public static string ToArgument(this SourceKind kind) => kind switch
    {
        SourceKind.Pdf => "pdf",
        SourceKind.Docx => "docx",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only PDF and Word files are read in a separate process."),
    };

    private static SourceKind? ParseKind(string argument) => argument switch
    {
        "pdf" => SourceKind.Pdf,
        "docx" => SourceKind.Docx,
        _ => null,
    };

    private static ExtractionLimits? ReadLimits()
    {
        var json = Environment.GetEnvironmentVariable(LimitsVariable);
        return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<ExtractionLimits>(json, ExtractionLimits.Json);
    }

    private static int Hang(string marker)
    {
        if (File.Exists("/bin/sh"))
        {
            var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("sleep 300; : \"$0\"");
            start.ArgumentList.Add(marker);
            Process.Start(start);
        }

        Thread.Sleep(Timeout.Infinite);
        return ExitOk;
    }

    private static int GrowManagedMemory()
    {
        var kept = new List<byte[]>();
        while (true)
            kept.Add(GC.AllocateArray<byte>(4 * 1_048_576, pinned: false));
    }

    private static int GrowNativeMemory()
    {
        const int block = 4 * 1_048_576;
        long total = 0;
        while (total < TestNativeMemoryCeiling)
        {
            var pointer = Marshal.AllocHGlobal(block);
            for (var offset = 0; offset < block; offset += 4_096)
                Marshal.WriteByte(pointer, offset, 1);
            total += block;
            Thread.Sleep(10);
        }

        Thread.Sleep(Timeout.Infinite);
        return ExitOk;
    }

    private static int Crash(int depth)
    {
        Span<byte> frame = stackalloc byte[1_024];
        frame[0] = (byte)depth;
        return Crash(depth + 1) + frame[0];
    }

    private static int Fail(TextWriter errors, int exitCode, string code)
    {
        errors.WriteLine(code);
        errors.Flush();
        return exitCode;
    }
}
