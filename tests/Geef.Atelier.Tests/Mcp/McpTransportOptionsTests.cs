using System.Text.Json;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// Since SDK 2.4.0 (D-086) the transport is stateless by an explicit setting, not by a default that a later SDK may change, and the
/// server announces nothing beyond tools: no tasks, no skills, no extensions.
/// </summary>
public sealed class McpTransportOptionsTests
{
    [Fact]
    public void The_transport_is_stateless_by_an_explicit_setting()
    {
        using var provider = McpTestServer.Provider("true", "On", TellOnceMode.WithoutPromise);

        var transport = provider.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;

        Assert.Equal(HttpServerSessionMode.Stateless, transport.SessionMode);
    }

    [Theory]
    [InlineData("true", "On")]
    [InlineData("true", "Off")]
    [InlineData("false", "On")]
    [InlineData("false", "Off")]
    public void No_tool_announces_task_support(string legacy, string tickets)
    {
        using var provider = McpTestServer.Provider(legacy, tickets, TellOnceMode.WithoutPromise);

        foreach (var line in McpTestServer.ToolListJson(provider).Split('\n'))
        {
            using var tool = JsonDocument.Parse(line);
            Assert.False(tool.RootElement.TryGetProperty("execution", out _), line[..Math.Min(80, line.Length)]);
        }
    }

    [Fact]
    public void The_server_announces_no_extensions()
    {
        using var provider = McpTestServer.Provider("true", "On", TellOnceMode.WithoutPromise);

        var options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;

        Assert.Null(options.Capabilities?.Extensions);
        Assert.Null(options.Capabilities?.Experimental);
    }
}
