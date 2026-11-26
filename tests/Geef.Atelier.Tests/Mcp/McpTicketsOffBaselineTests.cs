using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// With <c>Uploads:Tickets=Off</c> the tool list and the server instructions are the same, byte for byte, as before stage 6
/// (<c>main</c> at <c>aadcac5</c>), apart from the texts changed on purpose later: the hashes below were taken from that state, as clients
/// see it (<see cref="McpJsonUtilities.DefaultOptions"/>), and taken again once when changing a text lost the quick change <c>longer</c>
/// (<c>revise_order</c>, <c>get_order_result</c> with <c>lengthNotice</c>, step 6 of the instructions).
/// </summary>
public sealed class McpTicketsOffBaselineTests
{
    private const string ToolsWithLegacyHash = "ecc7c6f7d9382fdfe792b67abed7d6131146b54f0e96664d701199379df87fcc";
    private const string ToolsWithoutLegacyHash = "0fa78552ab744f286aa9bb10319398b5a613daf1b1f20fabc3cd114312a4e340";
    private const string InstructionsWithLegacyHash = "2a2bea1ecee187c978329c70268dcc752809088e6e16a11e3d90bc1cd7164d85";
    private const string InstructionsWithoutLegacyHash = "da4d6dcb34a7d7ad213774155080b212f2d4ef2eed30ca188c784a23cea8c928";

    [Theory]
    [InlineData("true", ToolsWithLegacyHash)]
    [InlineData("false", ToolsWithoutLegacyHash)]
    public void Without_tickets_the_tool_list_is_unchanged(string legacy, string expected)
    {
        using var provider = McpTestServer.Provider(legacy, tickets: null);

        var actual = Sha256(McpTestServer.ToolListJson(provider));
        Assert.True(expected == actual, $"tool list hash {actual}");
    }

    [Theory]
    [InlineData("true", InstructionsWithLegacyHash)]
    [InlineData("false", InstructionsWithoutLegacyHash)]
    public void Without_tickets_the_instructions_are_unchanged(string legacy, string expected)
    {
        using var provider = McpTestServer.Provider(legacy, tickets: null);

        var actual = Sha256(McpTestServer.Instructions(provider));
        Assert.True(expected == actual, $"instructions hash {actual}");
    }

    internal static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>The MCP server options as the host builds them, with every service of the tools registered as a stub.</summary>
internal static class McpTestServer
{
    public static ServiceProvider Provider(string legacy, string? tickets)
    {
        var settings = new Dictionary<string, string?> { ["Mcp:LegacyTools"] = legacy };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        if (tickets is not null)
            services.Configure<UploadOptions>(o => o.Tickets = Enum.Parse<UploadTicketsMode>(tickets));
        foreach (var type in typeof(McpAssemblyMarker).Assembly.ToolMethods().SelectMany(m => m.GetParameters()).Select(p => p.ParameterType)
                     .Where(t => t.IsInterface && !t.IsClientValue() && !t.IsSdkValue() && !t.IsGenericType).Distinct())
            services.AddScoped(type, _ => throw new NotSupportedException());
        services.AddAtelierMcp();
        return services.BuildServiceProvider();
    }

    public static IReadOnlyDictionary<string, McpServerTool> Tools(ServiceProvider provider) =>
        provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.ToDictionary(t => t.ProtocolTool.Name, StringComparer.Ordinal);

    public static string ToolListJson(ServiceProvider provider) =>
        string.Join("\n", Tools(provider).OrderBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => JsonSerializer.Serialize(t.Value.ProtocolTool, McpJsonUtilities.DefaultOptions)));

    public static string Instructions(ServiceProvider provider) =>
        provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ServerInstructions!;
}
