using System.Collections.Concurrent;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Sources;

/// <summary>
/// The real source services (<c>AddSources</c>) against the Postgres fixture and a temporary storage root, with a settable
/// clock and every log line captured, for the tests of upload tickets and refusal counting.
/// </summary>
internal sealed class UploadTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private UploadTestHost(ServiceProvider provider, string root, TestClock clock, CapturedLogs logs)
    {
        _provider = provider;
        Root = root;
        Clock = clock;
        Logs = logs;
    }

    public string Root { get; }

    public TestClock Clock { get; }

    public CapturedLogs Logs { get; }

    public IServiceProvider Services => _provider;

    public static UploadTestHost Start(PostgresFixture fixture, Action<UploadOptions>? uploads = null, Action<SourceOptions>? sources = null,
        Action<IServiceCollection>? replace = null, string? connectionString = null, bool ticketsOn = true)
    {
        var root = Path.Combine(Path.GetTempPath(), $"geef-uploads-{Guid.NewGuid():N}");
        var now = DateTimeOffset.UtcNow;
        var clock = new TestClock(new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero));
        var logs = new CapturedLogs();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        services.AddDbContext<AtelierDbContext>(o => o.UseNpgsql(connectionString ?? fixture.ConnectionString));
        services.AddScoped<SourceRepository>();
        services.AddScoped<ISourceRepository>(sp => sp.GetRequiredService<SourceRepository>());
        services.AddScoped<ISourceRetentionStore>(sp => sp.GetRequiredService<SourceRepository>());
        services.Configure<SourceOptions>(o =>
        {
            o.StorageRoot = root;
            o.ExtractionWorkerAssembly = typeof(Program).Assembly.Location;
            sources?.Invoke(o);
        });
        services.AddSingleton<TimeProvider>(clock);
        services.AddSources(new ConfigurationBuilder().Build());
        services.Configure<UploadOptions>(o =>
        {
            o.Tickets = ticketsOn ? UploadTicketsMode.On : UploadTicketsMode.Off;
            uploads?.Invoke(o);
        });
        replace?.Invoke(services);
        return new UploadTestHost(services.BuildServiceProvider(), root, clock, logs);
    }

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Captures every log line with its formatted message, its structured values and its exception.</summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _lines);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? string.Join(" ", pairs.Select(p => $"{p.Key}={p.Value}"))
                : string.Empty;
            lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} | {values} | {exception}");
        }
    }
}

/// <summary>A stream that waits before its first read until the test releases it.</summary>
internal sealed class HeldUploadStream(byte[] content) : MemoryStream(content)
{
    public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Reading.TrySetResult();
        await Release.Task.WaitAsync(cancellationToken);
        return await base.ReadAsync(buffer, cancellationToken);
    }
}

/// <summary>A stream that breaks off after its first few bytes.</summary>
internal sealed class BreakingUploadStream(byte[] content) : MemoryStream(content)
{
    private bool _served;

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_served)
            throw new IOException("The connection was closed.");
        _served = true;
        return base.ReadAsync(buffer[..Math.Min(buffer.Length, 4)], cancellationToken);
    }
}
