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
/// (<c>revise_order</c>, <c>get_order_result</c> with <c>lengthNotice</c>, step 6 of the instructions), and once more for the questions the
/// person did not answer in step 5 of the instructions (D-081), and once more for <c>profile</c>, which since D-083 can save four About me
/// fields, with the sentence about it in the instructions. With saving switched off the tool list and the instructions still give the hashes before D-083,
/// apart from D-086: the instructions were shortened below the 4,096 characters Claude Code keeps, and the details of changing a text moved into the
/// descriptions of <c>revise_order</c> and <c>get_order_result</c>, so all eight hashes were taken again once; nothing else in the tool list changed.
/// The four tool list hashes were taken again once more for the SDK 2.4.0 (D-086): SDK 1.3.0 marked every async tool with
/// <c>execution: {"taskSupport":"optional"}</c> although the server offers no tasks; 2.4.0 leaves the field out, and nothing else changed.
/// </summary>
public sealed class McpTicketsOffBaselineTests
{
    private const string ToolsWithLegacyHash = "6df3d78d078549a34a9b38d1c21f6f49b2d0e5a13814eb5e52a4118863f1119e";
    private const string ToolsWithoutLegacyHash = "863752b45a5537c880951e559c8b2591e22b1725c3753e5f111a047101ea7e44";
    private const string InstructionsWithLegacyHash = "0008d51c7b9a6d3663e355f88eb9a485ec4565a787d3b1476277452eb585d81a";
    private const string InstructionsWithoutLegacyHash = "f9c6023635e5f6d73305154507755c8c36d483ba989488706b9c0920d52d04d1";
    private const string ToolsWithLegacyBeforeAboutMeHash = "d41a8cc7025ba85b39f8311a45de938b6184148b9be2110d1d9ceb29d8ce25ad";
    private const string ToolsWithoutLegacyBeforeAboutMeHash = "d9065d358e2afd84b19f96f5062e7273863737798e6cfd109fbca3d41556cbbd";
    private const string InstructionsWithLegacyBeforeAboutMeHash = "8185e3bdcfa40031c0d4f0c5eca8cd08864a7ab11eb3cf2d0288203152eff680";
    private const string InstructionsWithoutLegacyBeforeAboutMeHash = "75aab43d645c8fadd9927f53851a12f8a53df7c86ef70114d03af033ccd7e09a";

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

    [Theory]
    [InlineData("true", ToolsWithLegacyBeforeAboutMeHash)]
    [InlineData("false", ToolsWithoutLegacyBeforeAboutMeHash)]
    public void Without_saving_about_me_details_the_tool_list_is_the_one_before_it(string legacy, string expected)
    {
        using var provider = McpTestServer.Provider(legacy, tickets: null, tellOnce: TellOnceMode.Off);

        var actual = Sha256(McpTestServer.ToolListJson(provider));
        Assert.True(expected == actual, $"tool list hash {actual}");
    }

    [Theory]
    [InlineData(true, InstructionsWithLegacyBeforeAboutMeHash)]
    [InlineData(false, InstructionsWithoutLegacyBeforeAboutMeHash)]
    public void Without_saving_about_me_details_the_instructions_are_those_before_it(bool legacy, string expected)
    {
        var actual = Sha256(McpServerInstructions.For(new OrderOptions().MaxWaitSeconds, legacy, tickets: false, aboutMeSaving: false));
        Assert.True(expected == actual, $"instructions hash {actual}");
    }

    internal static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>The MCP server options as the host builds them, with every service of the tools registered as a stub.</summary>
internal static class McpTestServer
{
    public static ServiceProvider Provider(string legacy, string? tickets, TellOnceMode? tellOnce = null, Action<IServiceCollection>? configure = null)
    {
        var settings = new Dictionary<string, string?> { ["Mcp:LegacyTools"] = legacy };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        if (tickets is not null)
            services.Configure<UploadOptions>(o => o.Tickets = Enum.Parse<UploadTicketsMode>(tickets));
        if (tellOnce is { } mode)
            services.Configure<OrderOptions>(o => o.TellOnce = mode);
        foreach (var type in typeof(McpAssemblyMarker).Assembly.ToolMethods().SelectMany(m => m.GetParameters()).Select(p => p.ParameterType)
                     .Where(t => t.IsInterface && !t.IsClientValue() && !t.IsSdkValue() && !t.IsGenericType).Distinct())
            services.AddScoped(type, _ => throw new NotSupportedException());
        configure?.Invoke(services);
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
