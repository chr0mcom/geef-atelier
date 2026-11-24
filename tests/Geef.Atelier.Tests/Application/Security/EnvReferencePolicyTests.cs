using Geef.Atelier.Application.Security;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Application.Security;

public sealed class EnvReferencePolicyTests
{
    private static EnvReferencePolicy Build(CapturingLogger? logger = null, params string[] extra) =>
        new(new StaticOptionsMonitor<EnvReferenceOptions>(new EnvReferenceOptions { Allowed = [.. extra] }),
            logger ?? new CapturingLogger());

    [Theory]
    [InlineData("OPENAI_API_KEY")]
    [InlineData("LLM_OPENROUTER_API_KEY")]
    [InlineData("TAVILY_API_KEY")]
    [InlineData("OLLAMA_ENDPOINT")]
    [InlineData("ATELIER_SECRET_MY_TOOL")]
    public void DefaultList_AllowsNamesInUse(string name) => Assert.True(Build().IsAllowed(name));

    [Theory]
    [InlineData("CLI_PROXY_API_KEY")]
    [InlineData("ConnectionStrings__DefaultConnection")]
    [InlineData("AtelierMcp__Token")]
    [InlineData("ATELIER_MCP_TOKEN")]
    [InlineData("ATELIER_PASSWORD_HASH")]
    [InlineData("POSTGRES_PASSWORD")]
    [InlineData("OAUTH_REGISTRATION_TOKEN")]
    [InlineData("openai_api_key")]
    [InlineData("ATELIER_SECRET_")]
    [InlineData("OPENAI_API_KEY ")]
    [InlineData("A-B")]
    [InlineData("")]
    [InlineData(null)]
    public void DefaultList_RejectsEverythingElse(string? name) => Assert.False(Build().IsAllowed(name));

    [Fact]
    public void ConfiguredEntries_ExtendTheDefaults()
    {
        var policy = Build(null, "MY_EXTRA_KEY", "TEAM_*");
        Assert.True(policy.IsAllowed("MY_EXTRA_KEY"));
        Assert.True(policy.IsAllowed("TEAM_SEARCH_TOKEN"));
        Assert.True(policy.IsAllowed("OPENAI_API_KEY"));
        Assert.False(policy.IsAllowed("TEAM"));
    }

    [Fact]
    public void LonePrefixStar_DoesNotOpenEverything()
    {
        Assert.False(Build(null, "*").IsAllowed("CLI_PROXY_API_KEY"));
    }

    [Fact]
    public void EnsureAllowed_PassesEmptyAndAllowed_RejectsOthersWithFieldName()
    {
        var policy = Build();
        policy.EnsureAllowed(null, "x");
        policy.EnsureAllowed("", "x");
        policy.EnsureAllowed("OPENAI_API_KEY", "x");

        var ex = Assert.Throws<EnvReferenceNotAllowedException>(() => policy.EnsureAllowed("CLI_PROXY_API_KEY", "API key variable"));
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
        Assert.StartsWith("API key variable:", ex.Message);
        Assert.Contains("CLI_PROXY_API_KEY", ex.Message);
    }

    [Fact]
    public void Read_ReturnsValueOnlyForAllowedNames_AndLogsIgnoredOnes()
    {
        var allowed   = "ATELIER_SECRET_POLICY_TEST_" + Guid.NewGuid().ToString("N");
        var forbidden = "GEEF_FORBIDDEN_POLICY_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(allowed, "allowed-value");
        Environment.SetEnvironmentVariable(forbidden, "forbidden-value");
        try
        {
            var logger = new CapturingLogger();
            var policy = Build(logger);

            Assert.Equal("allowed-value", policy.Read(allowed, "test"));
            Assert.Null(policy.Read(forbidden, "tool 'x' secret"));
            Assert.Null(policy.Read(null, "test"));

            var warning = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, warning.Level);
            Assert.Contains(forbidden, warning.Message);
            Assert.DoesNotContain("forbidden-value", warning.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(allowed, null);
            Environment.SetEnvironmentVariable(forbidden, null);
        }
    }

    [Fact]
    public void DefaultInstance_UsesDefaultList()
    {
        Assert.True(EnvReferencePolicy.Default.IsAllowed("OPENAI_API_KEY"));
        Assert.False(EnvReferencePolicy.Default.IsAllowed("CLI_PROXY_API_KEY"));
    }

    internal sealed class CapturingLogger : ILogger<EnvReferencePolicy>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
