using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Mcp;
using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The seams of the upload ticket over the real <c>Program</c> wiring: the <c>curlCommand</c> of <c>prepare_attachment</c> runs as it
/// is with the real <c>curl</c> in a POSIX shell against the real Kestrel server, also for file names with spaces, quotes, umlauts, a
/// leading dash, <c>$</c>, backticks and line breaks, and no part of a name is ever executed; the token appears in no address, no log
/// line and no answer; a running upload never outlives the request timeout, so the cleanup cannot release it; and with
/// <c>Uploads:Tickets=Off</c> the upload addresses answer exactly like addresses that never existed.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class UploadTicketSeamTests(PostgresFixture postgres)
{
    private const string PathPlaceholder = "'<path to the file>'";

    private static readonly Regex Command = new(
        @"^curl --fail-with-body -sS --globoff -T '<path to the file>' -H 'X-Upload-Token: (?<token>[A-Za-z0-9_-]{43})' (?<url>http://127\.0\.0\.1:[0-9]+/api/uploads/(?<id>[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}))$",
        RegexOptions.CultureInvariant);

    private static readonly string[] HostileNames =
    [
        "Offer with spaces.md",
        "it's \"quoted\" 'twice'.md",
        "Prüfung Größe ÄÖÜ ß é.md",
        "-leading-dash.md",
        "$HOME $(touch canary-dollar) ${PATH} $1.md",
        "`touch canary-backtick`.md",
        "a;b|c&d>e<f (touch canary-semicolon).md",
        "line\nbreak; touch canary-newline.md",
        "back\\slash *?[x] ~ !.md",
    ];

    [Fact]
    public async Task The_command_of_prepare_attachment_runs_as_it_is_with_real_curl_and_never_executes_a_file_name()
    {
        var port = FreePort();
        var issuer = $"http://127.0.0.1:{port}";
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.Configure<UploadOptions>(o => o.Tickets = UploadTicketsMode.On);
            services.Configure<OAuthOptions>(o => o.Issuer = issuer);
            services.Configure<SourceOptions>(o => o.MapMaxChars = 2_000_000);
        }, kestrelPort: port);
        var directory = Directory.CreateTempSubdirectory("geef-curl-").FullName;
        try
        {
            for (var index = 0; index < HostileNames.Length; index++)
            {
                var name = HostileNames[index];
                var path = Path.Combine(directory, name);
                var content = Encoding.UTF8.GetBytes($"# Upload {index}\n" + string.Concat(Enumerable.Repeat($"Line {index} of a plain test file. ", index == 0 ? 50_000 : 40)));
                await File.WriteAllBytesAsync(path, content);

                var prepared = await host.CallMcpToolAsync("prepare_attachment", new { fileName = name, sizeBytes = content.LongLength });
                var command = prepared.GetProperty("curlCommand").GetString()!;
                var match = Command.Match(command);
                Assert.True(match.Success, $"Unexpected command for '{name}': {command}");
                Assert.Equal(prepared.GetProperty("uploadId").GetString(), match.Groups["id"].Value);
                Assert.StartsWith(issuer + "/api/uploads/", match.Groups["url"].Value, StringComparison.Ordinal);

                var shell = await RunShellAsync(command.Replace(PathPlaceholder, ShellQuote(path), StringComparison.Ordinal), directory);
                Assert.True(shell.ExitCode == 0, $"curl failed for '{name}' ({shell.ExitCode}): {shell.Output} {shell.Errors}");
                using var answer = JsonDocument.Parse(shell.Output);
                Assert.Equal(match.Groups["id"].Value, answer.RootElement.GetProperty("uploadId").GetString());
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), answer.RootElement.GetProperty("sha256").GetString());
                Assert.True(answer.RootElement.GetProperty("usable").GetBoolean(), shell.Output);
            }

            var relative = await host.CallMcpToolAsync("prepare_attachment", new { fileName = "-leading-dash.md" });
            var relativeContent = Encoding.UTF8.GetBytes("# Relative\nThe second upload of a file whose name starts with a dash. " + Guid.NewGuid());
            await File.WriteAllBytesAsync(Path.Combine(directory, "-leading-dash.md"), relativeContent);
            var relativeRun = await RunShellAsync(relative.GetProperty("curlCommand").GetString()!.Replace(PathPlaceholder, "'-leading-dash.md'", StringComparison.Ordinal), directory);
            Assert.True(relativeRun.ExitCode == 0, $"curl failed for the relative path: {relativeRun.Output} {relativeRun.Errors}");
            using (var answer = JsonDocument.Parse(relativeRun.Output))
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(relativeContent)), answer.RootElement.GetProperty("sha256").GetString());

            Assert.Empty(Directory.EnumerateFileSystemEntries(directory, "canary-*", SearchOption.AllDirectories));
            Assert.Equal(HostileNames.Order(StringComparer.Ordinal), Directory.EnumerateFiles(directory).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Assert.Equal(HostileNames.Length + 1, await host.QueryAsync(db => db.Set<UploadTicketEntity>().CountAsync(t => t.State == UploadTicketState.Used)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task The_one_copy_and_upload_command_that_the_description_recommends_never_executes_a_file_name()
    {
        var port = FreePort();
        var issuer = $"http://127.0.0.1:{port}";
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.Configure<UploadOptions>(o => o.Tickets = UploadTicketsMode.On);
            services.Configure<OAuthOptions>(o => o.Issuer = issuer);
        }, kestrelPort: port);
        Assert.Contains("letters, digits and / . - _ * (a * in place of every other character)", PrepareAttachmentTool.QuotingRule, StringComparison.Ordinal);
        Assert.Contains("in ONE shell command", PrepareAttachmentTool.QuotingRule, StringComparison.Ordinal);
        Assert.Contains(PrepareAttachmentTool.CopyAndUploadCommand, PrepareAttachmentTool.QuotingRule, StringComparison.Ordinal);
        var directory = Directory.CreateTempSubdirectory("geef-glob-").FullName;
        var temp = Directory.CreateTempSubdirectory("geef-glob-tmp-").FullName;
        string[] names = [.. HostileNames, "r';id>PWNED;'.txt", "q\"; touch canary-quote; \".txt", "emoji 😀 \u202Eevil.md"];
        try
        {
            for (var index = 0; index < names.Length; index++)
            {
                var name = names[index];
                var path = Path.Combine(directory, name);
                var content = Encoding.UTF8.GetBytes($"# Copied {index}\nA plain test file copied under a plain name. " + Guid.NewGuid());
                await File.WriteAllBytesAsync(path, content);
                var pattern = PatternOf(path);
                Assert.Matches(@"^[\p{L}\p{Nd}/._*-]+$", pattern);

                var prepared = await host.CallMcpToolAsync("prepare_attachment", new { fileName = "upload.md" });
                var script = OneCommand(prepared.GetProperty("curlCommand").GetString()!, pattern);
                var shellName = index % 2 == 0 ? "/bin/sh" : "bash";
                var shell = await RunShellAsync(script, directory, shellName, temp);

                Assert.True(shell.ExitCode == 0, $"copy and upload failed in {shellName} for '{name}' ({shell.ExitCode}): {shell.Output} {shell.Errors}");
                using var answer = JsonDocument.Parse(shell.Output);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), answer.RootElement.GetProperty("sha256").GetString());
                Assert.True(answer.RootElement.GetProperty("usable").GetBoolean(), shell.Output);
                Assert.Equal("Used", await StateAsync(host, Guid.Parse(prepared.GetProperty("uploadId").GetString()!)));
                Assert.Empty(Directory.EnumerateFileSystemEntries(temp));
            }

            var twinsPrepared = await host.CallMcpToolAsync("prepare_attachment", new { fileName = "upload.md" });
            await File.WriteAllTextAsync(Path.Combine(directory, "twin a.md"), "# Twin A");
            await File.WriteAllTextAsync(Path.Combine(directory, "twin b.md"), "# Twin B");
            var twinsPattern = PatternOf(Path.Combine(directory, "twin x.md")).Replace("twin*x.md", "twin*.md", StringComparison.Ordinal);
            var twins = await RunShellAsync(OneCommand(twinsPrepared.GetProperty("curlCommand").GetString()!, twinsPattern), directory, "/bin/sh", temp);
            Assert.DoesNotContain("sha256", twins.Output, StringComparison.Ordinal);
            Assert.Equal("Open", await StateAsync(host, Guid.Parse(twinsPrepared.GetProperty("uploadId").GetString()!)));
            Assert.Empty(Directory.EnumerateFileSystemEntries(temp));

            Assert.Empty(Directory.EnumerateFileSystemEntries(directory, "canary-*", SearchOption.AllDirectories));
            Assert.Empty(Directory.EnumerateFileSystemEntries(directory, "PWNED", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task Copy_and_upload_split_into_two_commands_or_with_the_copy_in_single_quotes_upload_nothing()
    {
        var port = FreePort();
        var issuer = $"http://127.0.0.1:{port}";
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.Configure<UploadOptions>(o => o.Tickets = UploadTicketsMode.On);
            services.Configure<OAuthOptions>(o => o.Issuer = issuer);
        }, kestrelPort: port);
        var directory = Directory.CreateTempSubdirectory("geef-split-").FullName;
        var temp = Directory.CreateTempSubdirectory("geef-split-tmp-").FullName;
        try
        {
            var path = Path.Combine(directory, "Anna's notes (final).md");
            await File.WriteAllTextAsync(path, "# Notes\nA plain test file. " + Guid.NewGuid());
            var pattern = PatternOf(path);

            var split = await host.CallMcpToolAsync("prepare_attachment", new { fileName = "upload.md" });
            var copy = await RunShellAsync($"d=$(mktemp -d) && cp -- {pattern} {PrepareAttachmentTool.CopyPath}", directory, "/bin/sh", temp);
            Assert.Equal(0, copy.ExitCode);
            var upload = await RunShellAsync(split.GetProperty("curlCommand").GetString()!.Replace(PathPlaceholder, PrepareAttachmentTool.CopyPath, StringComparison.Ordinal), directory, "/bin/sh", temp);
            Assert.NotEqual(0, upload.ExitCode);
            Assert.DoesNotContain("sha256", upload.Output, StringComparison.Ordinal);
            Assert.Equal("Open", await StateAsync(host, Guid.Parse(split.GetProperty("uploadId").GetString()!)));

            var quoted = await host.CallMcpToolAsync("prepare_attachment", new { fileName = "upload.md" });
            var inSingleQuotes = PrepareAttachmentTool.CopyAndUploadCommand
                .Replace(PrepareAttachmentTool.PatternSlot, pattern, StringComparison.Ordinal)
                .Replace(PrepareAttachmentTool.CurlSlot, quoted.GetProperty("curlCommand").GetString()!.Replace(PathPlaceholder, "'$d/upload'", StringComparison.Ordinal), StringComparison.Ordinal);
            var single = await RunShellAsync(inSingleQuotes, directory, "/bin/sh", temp);
            Assert.DoesNotContain("sha256", single.Output, StringComparison.Ordinal);
            Assert.Equal("Open", await StateAsync(host, Guid.Parse(quoted.GetProperty("uploadId").GetString()!)));

            var whole = await host.CallMcpToolAsync("prepare_attachment", new { fileName = "upload.md" });
            var one = await RunShellAsync(OneCommand(whole.GetProperty("curlCommand").GetString()!, pattern), directory, "/bin/sh", temp);
            using var answer = JsonDocument.Parse(one.Output);
            Assert.True(answer.RootElement.GetProperty("usable").GetBoolean(), one.Output);
            Assert.Equal("Used", await StateAsync(host, Guid.Parse(whole.GetProperty("uploadId").GetString()!)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task The_token_appears_in_no_address_no_log_line_and_no_answer()
    {
        var logs = new CapturedLogs();
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.Configure<UploadOptions>(o => o.Tickets = UploadTicketsMode.On);
            services.Configure<OAuthOptions>(o => o.Issuer = "https://atelier.example");
            services.AddLogging(b =>
            {
                b.AddProvider(logs);
                b.SetMinimumLevel(LogLevel.Trace);
                b.AddFilter<CapturedLogs>(null, LogLevel.Trace);
                b.AddFilter(McpMessageLogGuard.CategoryPrefix, LogLevel.Trace);
                b.AddFilter<CapturedLogs>(McpMessageLogGuard.CategoryPrefix + ".Server", LogLevel.Trace);
            });
        });
        var bytes = Encoding.UTF8.GetBytes("# Notes\nThe delivery moves by two weeks. " + Guid.NewGuid());

        var prepared = await host.CallMcpToolAsync("prepare_attachment", new { fileName = "notes.md", sizeBytes = bytes.LongLength });
        var match = Regex.Match(prepared.GetProperty("curlCommand").GetString()!, "X-Upload-Token: (?<token>[A-Za-z0-9_-]{43})' (?<url>\\S+)$");
        Assert.True(match.Success, prepared.ToString());
        var token = match.Groups["token"].Value;
        var url = new Uri(match.Groups["url"].Value);
        Assert.Equal(string.Empty, url.Query);
        Assert.Equal(1, Regex.Count(prepared.ToString(), Regex.Escape(token)));
        Assert.DoesNotContain(token, prepared.GetProperty("uploadPage").GetString()!, StringComparison.Ordinal);
        Assert.DoesNotContain(token, prepared.GetProperty("next").GetString()!, StringComparison.Ordinal);

        var answers = new List<string>();
        using var client = host.CreateAnonymousClient();
        foreach (var (send, value) in new (byte[] Bytes, string Token)[]
                 {
                     (bytes, token[..^1] + (token[^1] == 'A' ? 'B' : 'A')),
                     (bytes, token + "x"),
                     (Encoding.UTF8.GetBytes("# Shorter"), token),
                     (bytes, token),
                     (bytes, token),
                 })
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, url.PathAndQuery) { Content = new ByteArrayContent(send) };
            request.Headers.Add(UploadTicketRoutes.TokenHeader, value);
            using var response = await client.SendAsync(request);
            answers.Add($"{(int)response.StatusCode} {response.Headers} {await response.Content.ReadAsStringAsync()}");
        }

        Assert.StartsWith("201 ", answers[3], StringComparison.Ordinal);
        Assert.StartsWith("404 ", answers[4], StringComparison.Ordinal);
        var submitted = await host.CallMcpToolAsync("submit_order", new { request = "Fasse die Notizen zusammen.", uploadIds = new[] { url.Segments[^1] } });
        host.ReleaseOrchestrator();
        answers.Add(submitted.ToString());

        Assert.All(answers, a => Assert.DoesNotContain(token, a, StringComparison.Ordinal));
        Assert.Contains(logs.Lines, l => l.StartsWith("Debug ModelContextProtocol", StringComparison.Ordinal) || l.StartsWith("Information ModelContextProtocol", StringComparison.Ordinal));
        Assert.Contains(logs.Lines, l => l.StartsWith("Trace ", StringComparison.Ordinal));
        Assert.DoesNotContain(logs.Lines, l => l.StartsWith("Trace ModelContextProtocol", StringComparison.Ordinal));
        Assert.Contains(logs.Lines, l => l.Contains(url.Segments[^1], StringComparison.Ordinal));
        var leaks = logs.Lines.Where(l => l.Contains(token, StringComparison.Ordinal)).Select(l => l.Replace(token, "<token>", StringComparison.Ordinal)).ToList();
        Assert.True(leaks.Count == 0, string.Join("\n", leaks.Select(l => l.Length > 300 ? l[..300] : l)));
        var rows = await host.QueryAsync(db => db.Database.SqlQuery<string>($"SELECT row_to_json(t)::text AS \"Value\" FROM \"UploadTickets\" t").ToListAsync());
        Assert.DoesNotContain(rows, r => r.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stalled_token_upload_ends_with_the_request_timeout_and_frees_its_ticket_long_before_the_cleanup()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.Configure<UploadOptions>(o => o.Tickets = UploadTicketsMode.On);
            services.Configure<SourceOptions>(o => o.UploadTimeout = TimeSpan.FromSeconds(2));
        });
        var owner = "stall-" + Guid.NewGuid().ToString("N")[..6];
        var prepared = await host.WithScopeAsync(sp => sp.GetRequiredService<IUploadTicketService>().PrepareAsync(owner, "slow.md", null));
        using var client = host.CreateAnonymousClient();
        var stall = new StalledContent(Encoding.UTF8.GetBytes("# Slow\nThe first bytes arrive, then nothing more."));

        var watch = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/uploads/{prepared.UploadId}") { Content = stall };
        request.Headers.Add(UploadTicketRoutes.TokenHeader, prepared.Token);
        var sending = client.SendAsync(request);
        await stall.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var receiving = Stopwatch.StartNew();
        while (await StateAsync(host, prepared.UploadId) != "Receiving" && receiving.Elapsed < TimeSpan.FromSeconds(10) && !sending.IsCompleted)
            await Task.Delay(20);
        Assert.Equal("Receiving", await StateAsync(host, prepared.UploadId));

        var finished = await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(30)));
        stall.Stop.TrySetResult();
        Assert.Same(sending, finished);
        using (var response = await sending)
            Assert.True(response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.BadRequest, $"{(int)response.StatusCode}");
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), watch.Elapsed.ToString());
        Assert.Equal("Open", await StateAsync(host, prepared.UploadId));
        Assert.Equal(0, await host.QueryAsync(db => db.Set<UploadTicketEntity>().Where(t => t.Id == prepared.UploadId).Select(t => t.Attempts).SingleAsync()));
        Assert.Equal(0, await host.QueryAsync(db => db.Sources.CountAsync(s => s.OwnerUsername == owner)));
    }

    [Fact]
    public async Task With_tickets_off_the_upload_addresses_answer_exactly_like_addresses_that_never_existed()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var id = Guid.NewGuid();
        var anonymous = host.CreateAnonymousClient();
        var signedIn = await host.CreateSignedInClientAsync("off-" + Guid.NewGuid().ToString("N")[..6]);
        var antiforgery = await host.GetAntiforgeryTokenAsync(signedIn);

        foreach (var (client, who) in new[] { (anonymous, "anonymous"), (signedIn, "signed in") })
        {
            foreach (var (method, upload, never) in new[]
                     {
                         ("GET", $"/u/{id}", $"/u-never/{id}"),
                         ("PUT", $"/api/uploads/{id}", $"/api/uploads-never/{id}"),
                         ("POST", $"/api/uploads/{id}/page", $"/api/uploads-never/{id}/page"),
                         ("GET", $"/api/uploads/{id}", $"/api/uploads-never/{id}"),
                     })
            {
                var expected = await DescribeAsync(client, method, never, antiforgery);
                var actual = await DescribeAsync(client, method, upload, antiforgery);
                Assert.True(expected == actual, $"{who} {method} {upload}:\n  upload address: {actual}\n  never existed:  {expected}");
                Assert.False(actual.StartsWith('2') || actual.StartsWith('3'), $"{who} {method} {upload}: {actual}");
            }
        }
    }

    private static async Task<string> DescribeAsync(HttpClient client, string method, string path, string antiforgery)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method != "GET")
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes("# Note"));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/markdown");
            form.Add(file, "file", "note.md");
            request.Content = form;
            request.Headers.Add(UploadTicketRoutes.TokenHeader, new string('a', 43));
            request.Headers.Add(AtelierEndToEndHost.AntiforgeryHeader, antiforgery);
        }

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        return $"{(int)response.StatusCode} {response.Content.Headers.ContentType} {response.Headers.Location} {response.Headers.WwwAuthenticate} {body}";
    }

    private static Task<string> StateAsync(AtelierEndToEndHost host, Guid id) =>
        host.QueryAsync(db => db.Database.SqlQuery<string>($"SELECT \"State\" AS \"Value\" FROM \"UploadTickets\" WHERE \"Id\" = {id}").SingleAsync());

    private static string PatternOf(string path) => Regex.Replace(path, @"[^\p{L}\p{Nd}/._-]+", "*");

    private static string ShellQuote(string value) => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string OneCommand(string curlCommand, string pattern) =>
        PrepareAttachmentTool.CopyAndUploadCommand
            .Replace(PrepareAttachmentTool.PatternSlot, pattern, StringComparison.Ordinal)
            .Replace(PrepareAttachmentTool.CurlSlot, curlCommand.Replace(PathPlaceholder, PrepareAttachmentTool.CopyPath, StringComparison.Ordinal), StringComparison.Ordinal);

    private static async Task<(int ExitCode, string Output, string Errors)> RunShellAsync(string command, string directory, string shell = "/bin/sh", string? temp = null)
    {
        var start = new ProcessStartInfo(shell)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add(command);
        start.Environment["HOME"] = directory;
        if (temp is not null)
            start.Environment["TMPDIR"] = temp;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await output, await errors);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class StalledContent(byte[] head) : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Stop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(head);
            await stream.FlushAsync();
            Started.TrySetResult();
            await Stop.Task;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
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
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(" ", pairs.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
                lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} | {values} | {exception}");
            }
        }
    }
}
