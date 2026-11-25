using System.Reflection;
using System.Text.Json;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp;
using Geef.Atelier.Mcp.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// The tool list of the stage-5 contract (4.4): exactly the nine order tools, the older tools beside them only with
/// <c>Mcp:LegacyTools=true</c>, the five with a successor pointing to it, and the longest wait named from <c>Orders:MaxWaitSeconds</c>.
/// </summary>
public sealed class McpToolCatalogTests
{
    private static readonly string[] OrderToolNames =
        ["submit_order", "answer_order", "get_order_status", "get_order_result", "revise_order", "export_order", "cancel_order", "list_orders", "profile"];

    private static readonly Dictionary<string, string> Successors = new(StringComparer.Ordinal)
    {
        ["submit_request"] = "submit_order",
        ["get_run_status"] = "get_order_status",
        ["get_run_result"] = "get_order_result",
        ["cancel_run"] = "cancel_order",
        ["list_runs"] = "list_orders",
    };

    [Fact]
    public void Every_tool_type_of_the_assembly_is_in_exactly_one_list()
    {
        var all = typeof(McpAssemblyMarker).Assembly.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null).ToList();

        Assert.Equal(9, McpToolCatalog.OrderTools.Count);
        Assert.Equal(19, McpToolCatalog.LegacyTools.Count);
        Assert.Empty(McpToolCatalog.OrderTools.Intersect(McpToolCatalog.LegacyTools));
        Assert.Equal(all.OrderBy(t => t.Name), McpToolCatalog.OrderTools.Concat(McpToolCatalog.LegacyTools).OrderBy(t => t.Name));
        Assert.Equal(all.Count, McpToolCatalog.OrderTools.Concat(McpToolCatalog.LegacyTools).ToolMethods().Count);
    }

    [Fact]
    public void With_legacy_tools_the_server_offers_the_nine_order_tools_and_every_older_tool()
    {
        var tools = Tools(legacy: "true");

        Assert.Equal(28, tools.Count);
        Assert.Equal(OrderToolNames.Order(StringComparer.Ordinal), tools.Keys.Intersect(OrderToolNames).Order(StringComparer.Ordinal));
        Assert.Equal(typeof(McpAssemblyMarker).Assembly.ToolMethods().Count, tools.Count);
    }

    [Fact]
    public void Without_legacy_tools_the_server_offers_exactly_the_nine_order_tools()
    {
        var tools = Tools(legacy: "false");

        Assert.Equal(OrderToolNames.Order(StringComparer.Ordinal), tools.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Without_configuration_the_older_tools_stay()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtelierMcp();
        using var provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<IOptions<McpOptions>>().Value.LegacyTools);
        Assert.Equal(28, provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Count);
    }

    [Fact]
    public void The_five_older_tools_with_a_successor_name_it_first_and_the_others_do_not()
    {
        var tools = Tools(legacy: "true");

        foreach (var (old, successor) in Successors)
            Assert.StartsWith($"Older tool, kept for now: use {successor} instead. ", tools[old].ProtocolTool.Description, StringComparison.Ordinal);
        Assert.All(tools.Where(t => !Successors.ContainsKey(t.Key)),
            t => Assert.DoesNotContain("Older tool", t.Value.ProtocolTool.Description ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void The_older_tools_keep_their_parameters()
    {
        var tools = Tools(legacy: "true");

        Assert.Equal(["briefingText", "configJson", "crewTemplate", "customCrew", "attachments"], Properties(tools["submit_request"]));
        Assert.Equal(["runId"], Properties(tools["get_run_status"]));
        Assert.Equal(["runId"], Properties(tools["cancel_run"]));
        Assert.Equal(["limit", "statusFilter", "includeAllUsers"], Properties(tools["list_runs"]));
    }

    [Fact]
    public void The_parameters_of_the_new_tools_match_the_contract()
    {
        var tools = Tools(legacy: "false");

        Assert.Equal(["orderId", "change", "quick"], Properties(tools["revise_order"]));
        Assert.Equal(["orderId"], Required(tools["revise_order"]));
        Assert.Contains("array", Types(tools["revise_order"], "quick"));
        Assert.Equal(["orderId", "format", "version"], Properties(tools["export_order"]));
        Assert.Equal(["orderId"], Required(tools["export_order"]));
        Assert.Equal(["orderId"], Properties(tools["cancel_order"]));
        Assert.Equal(["limit"], Properties(tools["list_orders"]));
        Assert.Empty(Required(tools["list_orders"]));
        Assert.Empty(Properties(tools["profile"]));
    }

    [Fact]
    public void The_reading_tools_are_marked_read_only_and_profile_has_no_way_to_write()
    {
        var tools = Tools(legacy: "false");

        Assert.All(new[] { "export_order", "list_orders", "profile" }, name => Assert.True(tools[name].ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.Null(tools["revise_order"].ProtocolTool.Annotations?.ReadOnlyHint);
        var profile = typeof(ProfileTool).GetMethod(nameof(ProfileTool.Profile))!;
        Assert.DoesNotContain(profile.GetParameters(), p => p.ParameterType.IsClientValue());
        Assert.Contains("About me", tools["profile"].ProtocolTool.Description, StringComparison.Ordinal);
        Assert.Contains("cannot change", tools["profile"].ProtocolTool.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Revise_order_asks_for_the_persons_words_unchanged_and_names_the_next_step()
    {
        var description = Tools(legacy: "false")["revise_order"].ProtocolTool.Description!;

        Assert.Contains("get_order_status", description, StringComparison.Ordinal);
        Assert.Contains("word for word", description, StringComparison.Ordinal);
        Assert.Contains("do not rephrase", description, StringComparison.Ordinal);
        Assert.Contains("do not change the text yourself", description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("crew")]
    [InlineData("grounding")]
    [InlineData("iteration")]
    [InlineData("convergence")]
    [InlineData(" run ")]
    [InlineData("runId")]
    public void The_new_tools_speak_without_internal_words(string word)
    {
        foreach (var description in new[]
                 {
                     ReviseOrderTool.ToolDescription, ExportOrderTool.ToolDescription, CancelOrderTool.ToolDescription, ListOrdersTool.ToolDescription,
                     ProfileTool.ToolDescription, ReviseOrderTool.StartedMessage, ReviseOrderTool.BusyMessage, CancelOrderTool.StoppingMessage,
                 })
            Assert.DoesNotContain(word, description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_longest_wait_in_the_texts_follows_orders_max_wait_seconds()
    {
        var shipped = Provider(legacy: "true");
        var shorter = Provider(legacy: "true", maxWait: "25");

        Assert.Contains("max 50", ToolsOf(shipped)["get_order_status"].ProtocolTool.Description, StringComparison.Ordinal);
        Assert.Contains("waitSeconds (up to 50)", Instructions(shipped), StringComparison.Ordinal);
        Assert.Equal(McpServerInstructions.Text, Instructions(shipped));
        Assert.Equal(GetOrderStatusTool.ToolDescription, ToolsOf(shipped)["get_order_status"].ProtocolTool.Description);

        Assert.Contains("(default 25, max 25)", ToolsOf(shorter)["get_order_status"].ProtocolTool.Description, StringComparison.Ordinal);
        Assert.Contains("waitSeconds (up to 25)", Instructions(shorter), StringComparison.Ordinal);
        Assert.DoesNotContain("50", ToolsOf(shorter)["get_order_status"].ProtocolTool.Description, StringComparison.Ordinal);
        Assert.Equal("(default 10, max 10)", GetOrderStatusTool.Describe(10).Split(" for a change")[0].Split("waitSeconds ")[1]);
    }

    [Fact]
    public void The_wait_parameter_names_no_fixed_limit()
    {
        var description = Description(Tools(legacy: "false")["get_order_status"], "waitSeconds");

        Assert.DoesNotContain("50", description, StringComparison.Ordinal);
        Assert.Contains("description of this tool", description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_instructions_mention_the_older_tools_only_while_they_are_offered()
    {
        Assert.EndsWith(McpServerInstructions.LegacyLine, Instructions(Provider(legacy: "true")), StringComparison.Ordinal);
        var without = Instructions(Provider(legacy: "false"));
        Assert.DoesNotContain("submit_request", without, StringComparison.Ordinal);
        Assert.DoesNotContain("older interface", without, StringComparison.Ordinal);
        Assert.EndsWith("do not add it.", without, StringComparison.Ordinal);
    }

    private static Dictionary<string, McpServerTool> Tools(string legacy) => ToolsOf(Provider(legacy));

    private static Dictionary<string, McpServerTool> ToolsOf(ServiceProvider provider) =>
        provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.ToDictionary(t => t.ProtocolTool.Name, StringComparer.Ordinal);

    private static string Instructions(ServiceProvider provider) =>
        provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ServerInstructions!;

    private static ServiceProvider Provider(string legacy, string? maxWait = null)
    {
        var settings = new Dictionary<string, string?> { ["Mcp:LegacyTools"] = legacy };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        if (maxWait is not null)
            services.Configure<OrderOptions>(o => o.MaxWaitSeconds = int.Parse(maxWait, System.Globalization.CultureInfo.InvariantCulture));
        foreach (var type in typeof(McpAssemblyMarker).Assembly.ToolMethods().SelectMany(m => m.GetParameters()).Select(p => p.ParameterType)
                     .Where(t => t.IsInterface && !t.IsClientValue() && !t.IsSdkValue() && !t.IsGenericType).Distinct())
            services.AddScoped(type, _ => throw new NotSupportedException());
        services.AddAtelierMcp();
        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<string> Properties(McpServerTool tool) =>
        tool.ProtocolTool.InputSchema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().Select(p => p.Name).ToList()
            : [];

    private static IReadOnlyList<string> Required(McpServerTool tool) =>
        tool.ProtocolTool.InputSchema.TryGetProperty("required", out var required) ? required.EnumerateArray().Select(r => r.GetString()!).ToList() : [];

    private static IReadOnlyList<string> Types(McpServerTool tool, string property)
    {
        var type = tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty(property).GetProperty("type");
        return type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(t => t.GetString()!).ToList() : [type.GetString()!];
    }

    private static string Description(McpServerTool tool, string property) =>
        tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty(property).GetProperty("description").GetString()!;
}
