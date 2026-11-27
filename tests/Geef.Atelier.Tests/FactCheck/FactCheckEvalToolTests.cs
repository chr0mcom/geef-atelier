using System.Net;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tools.FactCheckEval;

namespace Geef.Atelier.Tests.FactCheck;

/// <summary>Command line, Codex guard, call cap, gateway routes, split and amount reading of <c>tools/FactCheckEval</c>.</summary>
public sealed class FactCheckEvalToolTests
{
    [Fact]
    public void The_call_cap_is_required_and_bounded()
    {
        var line = CommandLine.Parse(["--corpus", "c.jsonl"], ["corpus", "max-calls"]);

        Assert.Throws<UsageException>(() => line.Number("max-calls", 1, 52));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["--max-calls", "53"], ["max-calls"]).Number("max-calls", 1, 52));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["--max-calls", "0"], ["max-calls"]).Number("max-calls", 1, 52));
        Assert.Equal(7, CommandLine.Parse(["--max-calls", "7"], ["max-calls"]).Number("max-calls", 1, 52));
    }

    [Fact]
    public void Only_declared_options_are_accepted_so_a_key_cannot_be_passed_as_an_argument()
    {
        Assert.Throws<UsageException>(() => CommandLine.Parse(["--api-key", "secret"], ["corpus"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["--corpus"], ["corpus"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(["--corpus", "--out"], ["corpus", "out"]));
    }

    [Fact]
    public void Lock_files_may_be_given_more_than_once_other_options_not()
    {
        var line = CommandLine.Parse(["--codex-lock", "/a", "--codex-lock", "/b", "--out", "x", "--out", "y"], ["codex-lock", "out"]);

        Assert.Equal(["/a", "/b"], line.All("codex-lock"));
        Assert.Throws<UsageException>(() => line.Optional("out"));
    }

    [Fact]
    public void The_key_comes_only_from_the_environment()
    {
        Assert.Equal("k", CommandLine.ApiKey(name => name == CommandLine.ApiKeyVariable ? "k" : null));
        Assert.Null(CommandLine.ApiKey(_ => ""));
    }

    [Fact]
    public void The_guard_blocks_on_any_lock_file_on_a_codex_process_and_when_pgrep_fails()
    {
        var dir = Directory.CreateTempSubdirectory("codex-guard-").FullName;
        try
        {
            var present = Path.Combine(dir, "codex-gesperrt");
            var absent = Path.Combine(dir, "other");

            Assert.Null(new CodexGuard([absent], () => string.Empty).Blocker());
            File.WriteAllText(present, string.Empty);
            Assert.Contains(present, new CodexGuard([absent, present], () => string.Empty).Blocker());
            Assert.Contains("Codex process", new CodexGuard([absent], () => "4711 codex exec\n").Blocker());
            Assert.Contains("Codex process", new CodexGuard([absent], () => "4712 /usr/bin/codex -c a=b --search exec -\n").Blocker());
            Assert.Null(new CodexGuard([absent], () => "4713 /home/x/.vscode-server/extensions/e/bin/codex -c features.x=true app-server\n").Blocker());
            Assert.Contains("could not be read", new CodexGuard([absent], () => null).Blocker());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void The_budget_counts_this_run_and_the_file()
    {
        var budget = new CallBudget(maxCalls: 5, ceiling: 20, alreadyUsed: 17);

        Assert.Equal(3, budget.Remaining);
        Assert.True(budget.TryTake() && budget.TryTake() && budget.TryTake());
        Assert.False(budget.TryTake());
        Assert.Equal(3, budget.Used);
    }

    [Fact]
    public void Every_subscription_cli_has_its_gateway_route_and_other_providers_are_refused()
    {
        Assert.Equal("http://cli-proxy:8090/v1/claude", GatewayResolver.Route("http://cli-proxy:8090/", ModelMatrix.ClaudeCli));
        Assert.Equal("http://cli-proxy:8090/v1/codex", GatewayResolver.Route("http://cli-proxy:8090", ModelMatrix.CodexCli));
        Assert.Throws<UsageException>(() => GatewayResolver.Route("http://cli-proxy:8090", "openrouter"));
    }

    [Fact]
    public async Task The_gateway_key_travels_only_in_the_authorization_header_and_failover_can_be_switched_off()
    {
        HttpRequestMessage? sent = null;
        string? body = null;
        using var http = new HttpClient(new Handler(async r =>
        {
            sent = r;
            body = await r.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1}}"""),
            };
        }));
        var resolver = new GatewayResolver(http, "http://gateway", "geheim-123");

        var (client, model, _) = resolver.ForProfile(ModelMatrix.CodexCli, ModelMatrix.CodexAstraAlias, 100);
        await client.CompleteAsync(new LlmRequest { Model = model, SystemPrompt = "s", UserPrompt = "u", DisableFailover = true }, CancellationToken.None);

        Assert.Equal("http://gateway/v1/codex/chat/completions", sent!.RequestUri!.ToString());
        Assert.Equal("Bearer geheim-123", sent.Headers.Authorization!.ToString());
        Assert.Equal("same-model", sent.Headers.GetValues("x-cli-proxy-failover").Single());
        Assert.DoesNotContain("geheim-123", body);
    }

    [Theory]
    [InlineData("split-vector-1", "dev")]
    [InlineData("split-vector-2", "holdout")]
    public void The_split_is_the_same_as_in_the_python_generator(string text, string split) =>
        Assert.Equal(split, EvalBasics.SplitOf(text));

    [Theory]
    [InlineData("1.234,50 €", "1234.50")]
    [InlineData("$1,234.50", "1234.50")]
    [InlineData("22,50 €", "22.50")]
    [InlineData("Summe: 72 €", "72")]
    [InlineData("1.234", "1234")]
    [InlineData("−5", "-5")]
    [InlineData("12.5", "12.5")]
    [InlineData("keine Zahl", null)]
    public void Amounts_are_read_like_the_python_reader(string text, string? value) =>
        Assert.Equal(value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Amounts.Parse(text));

    [Fact]
    public void The_fingerprint_changes_with_every_part()
    {
        var hash = EvalBasics.Fingerprint("a", "b");

        Assert.Equal(hash, EvalBasics.Fingerprint("a", "b"));
        Assert.NotEqual(hash, EvalBasics.Fingerprint("a", "c"));
        Assert.NotEqual(hash, EvalBasics.Fingerprint("ab", ""));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => answer(request);
    }

    [Fact]
    public void The_help_text_names_the_shipped_time_limit_of_the_checking_model()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Geef.Atelier.slnx")))
            root = root.Parent;
        Assert.NotNull(root);
        var program = File.ReadAllText(Path.Combine(root.FullName, "tools", "FactCheckEval", "Program.cs"));
        var seconds = (int)new Geef.Atelier.Core.Configuration.DirectOptions().FactCheckTimeout.TotalSeconds;

        Assert.Equal(90, seconds);
        Assert.Contains($"[--timeout-seconds {seconds}]", program, StringComparison.Ordinal);
        Assert.Contains($"[--production-timeout-seconds {seconds}]", program, StringComparison.Ordinal);
    }
}
