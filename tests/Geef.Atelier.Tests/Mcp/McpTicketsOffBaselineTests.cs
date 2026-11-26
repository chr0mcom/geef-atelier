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
    private const string ToolsWithLegacyHash = "db658fcddf55eeab54412dcb5d988294421dc793359281a45492e3dec9f1c840";
    private const string ToolsWithoutLegacyHash = "165e018513d9f4c28ab23e7b103c71cda389761a326e13804832554f754ed42a";
    private const string InstructionsWithLegacyHash = "c5ef1b77a1558dad0baef3e36a98eb829ea6c7e675c0ef87579216ac91eedb6b";
    private const string InstructionsWithoutLegacyHash = "44542b8183b3938d76674e840d8538df97e7f7d9c06fbf9d62a3f257cb7afa6a";

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
