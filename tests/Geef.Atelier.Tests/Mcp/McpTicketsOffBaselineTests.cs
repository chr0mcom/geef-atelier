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
/// The four tool list hashes were taken again once more for <c>givenAnswers</c> (D-088): the description of <c>get_order_result</c> gained
/// <see cref="Geef.Atelier.Mcp.Tools.GetOrderResultTool.GivenAnswersRule"/>; without that sentence the tool list gives the hashes before it.
/// The instructions did not change.
/// The two tool list hashes with the older tools were taken again once more for stage 8c (D-089): the template studio is gone, and
/// <c>analyze_template_proposal</c> and <c>materialize_template_proposal</c> stay as stubs that only return an error naming
/// <c>submit_order</c>. The hashes without the older tools and the instructions did not change.
/// Stage 8d (D-091) removed the older tools and <c>Mcp:LegacyTools</c>: the list always holds the order tools and the five stubs
/// <c>submit_request</c>, <c>submit_auto_crew_request</c> and <c>get_run_*</c>, and the line about the older tools left the instructions,
/// so one tool list hash and one instructions hash remain for each of the two states, taken again once; the instructions hashes are those
/// that were taken without the older tools before. In the same stage <c>submit_order</c> lost the sentence about <c>extraCareful</c> and the
/// parameter itself while "Extra careful" is offered to nobody (the default); with it offered, both come back.
/// </summary>
public sealed class McpTicketsOffBaselineTests
{
    private const string ToolsHash = "d3bbf029e664982befaf3055632a64ffffc40c4abaea5078da2f1ce82ede4b8a";
    private const string InstructionsHash = InstructionsWithoutLegacyHash;
    private const string ToolsBeforeAboutMeHash = "42c62f5b2bb9e313e9f03e03b7ed66147d8ef36afdedb1c614dd210239a62044";
    private const string InstructionsBeforeAboutMeHash = InstructionsWithoutLegacyBeforeAboutMeHash;
    private const string InstructionsWithoutLegacyHash = "f9c6023635e5f6d73305154507755c8c36d483ba989488706b9c0920d52d04d1";
    private const string InstructionsWithoutLegacyBeforeAboutMeHash = "75aab43d645c8fadd9927f53851a12f8a53df7c86ef70114d03af033ccd7e09a";

    [Fact]
    public void Without_tickets_the_tool_list_is_unchanged()
    {
        using var provider = McpTestServer.Provider(tickets: null);

        var actual = Sha256(McpTestServer.ToolListJson(provider));
        Assert.True(ToolsHash == actual, $"tool list hash {actual}");
    }

    [Fact]
    public void Without_tickets_the_instructions_are_unchanged()
    {
        using var provider = McpTestServer.Provider(tickets: null);

        var actual = Sha256(McpTestServer.Instructions(provider));
        Assert.True(InstructionsHash == actual, $"instructions hash {actual}");
    }

    [Fact]
    public void Without_saving_about_me_details_the_tool_list_is_the_one_before_it()
    {
        using var provider = McpTestServer.Provider(tickets: null, tellOnce: TellOnceMode.Off);

        var actual = Sha256(McpTestServer.ToolListJson(provider));
        Assert.True(ToolsBeforeAboutMeHash == actual, $"tool list hash {actual}");
    }

    [Fact]
    public void Without_saving_about_me_details_the_instructions_are_those_before_it()
    {
        var actual = Sha256(McpServerInstructions.For(new OrderOptions().MaxWaitSeconds, tickets: false, aboutMeSaving: false));
        Assert.True(InstructionsBeforeAboutMeHash == actual, $"instructions hash {actual}");
    }

    internal static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

/// <summary>The MCP server options as the host builds them, with every service of the tools registered as a stub.</summary>
internal static class McpTestServer
{
    public static ServiceProvider Provider(string? tickets, TellOnceMode? tellOnce = null, Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
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
