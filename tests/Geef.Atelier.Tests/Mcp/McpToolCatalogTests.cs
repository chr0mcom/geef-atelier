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
/// The tool list of the stage-5 contract (4.4): the nine order tools and, for one version, the five stubs of the removed older tools
/// (D-091), each naming its replacement, and the longest wait named from <c>Orders:MaxWaitSeconds</c>.
/// Without configuration upload tickets are off, so the ticket tool of stage 6 is not offered here.
/// </summary>
public sealed class McpToolCatalogTests
{
    private static readonly string[] OrderToolNames =
        ["submit_order", "answer_order", "get_order_status", "get_order_result", "revise_order", "export_order", "cancel_order", "list_orders", "profile"];

    private static readonly Dictionary<string, string> Successors = new(StringComparer.Ordinal)
    {
        ["submit_request"] = "submit_order",
        ["submit_auto_crew_request"] = "submit_order",
        ["get_run_status"] = "get_order_status",
        ["get_run_result"] = "get_order_result",
        ["get_run_details"] = "get_order_result",
    };

    [Fact]
    public void Every_tool_type_of_the_assembly_is_in_exactly_one_list()
    {
        var all = typeof(McpAssemblyMarker).Assembly.GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null).ToList();

        Assert.Equal(9, McpToolCatalog.OrderTools.Count);
        Assert.Equal([typeof(PrepareAttachmentTool)], McpToolCatalog.TicketTools);
        Assert.Equal([typeof(RetiredTools)], McpToolCatalog.RetiredTools);
        var lists = McpToolCatalog.OrderTools.Concat(McpToolCatalog.TicketTools).Concat(McpToolCatalog.RetiredTools).ToList();
        Assert.Equal(lists.Count, lists.Distinct().Count());
        Assert.Equal(all.OrderBy(t => t.Name), lists.OrderBy(t => t.Name));
        Assert.Equal(15, typeof(McpAssemblyMarker).Assembly.ToolMethods().Count);
    }

    [Fact]
    public void The_server_offers_the_nine_order_tools_and_the_five_stubs()
    {
        var tools = Tools();

        Assert.Equal(14, tools.Count);
        Assert.Equal(OrderToolNames.Concat(Successors.Keys).Order(StringComparer.Ordinal), tools.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("prepare_attachment", tools.Keys);
    }

    [Fact]
    public void Without_configuration_the_stubs_are_offered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtelierMcp();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(14, provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Count);
    }

    [Fact]
    public void Every_stub_names_its_replacement_and_the_order_tools_name_no_removed_tool()
    {
        var tools = Tools();

        foreach (var (old, successor) in Successors)
            Assert.Equal($"Removed: use {successor} instead. Every call returns an error that names it.", tools[old].ProtocolTool.Description);
        Assert.All(tools.Where(t => !Successors.ContainsKey(t.Key)),
            t => Assert.DoesNotContain("Removed", t.Value.ProtocolTool.Description ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void The_stubs_keep_the_parameters_of_the_removed_tools_and_require_none()
    {
        var tools = Tools();

        Assert.Equal(["briefingText", "configJson", "crewTemplate", "customCrew", "attachments"], Properties(tools["submit_request"]));
        Assert.Equal(["briefingText", "configJson", "chainToTaskRun", "attachments"], Properties(tools["submit_auto_crew_request"]));
        Assert.Equal(["runId"], Properties(tools["get_run_status"]));
        Assert.Equal(["runId"], Properties(tools["get_run_result"]));
        Assert.Equal(["runId"], Properties(tools["get_run_details"]));
        Assert.All(Successors.Keys, name => Assert.Empty(Required(tools[name])));
    }

    [Fact]
    public void The_parameters_of_the_new_tools_match_the_contract()
    {
        var tools = Tools();

        Assert.Equal(["orderId", "change", "quick"], Properties(tools["revise_order"]));
        Assert.Equal(["orderId"], Required(tools["revise_order"]));
        Assert.Contains("array", Types(tools["revise_order"], "quick"));
        Assert.Equal(["orderId", "format", "version"], Properties(tools["export_order"]));
        Assert.Equal(["orderId"], Required(tools["export_order"]));
        Assert.Equal(["orderId"], Properties(tools["cancel_order"]));
        Assert.Equal(["limit"], Properties(tools["list_orders"]));
        Assert.Empty(Required(tools["list_orders"]));
        Assert.Equal(["name", "signature", "formality", "language"], Properties(tools["profile"]));
        Assert.Empty(Required(tools["profile"]));
    }

    [Fact]
    public void The_reading_tools_are_marked_read_only_and_profile_writes_only_the_four_about_me_fields()
    {
        var tools = Tools();

        Assert.All(new[] { "export_order", "list_orders" }, name => Assert.True(tools[name].ProtocolTool.Annotations?.ReadOnlyHint));
        Assert.Null(tools["revise_order"].ProtocolTool.Annotations?.ReadOnlyHint);
        var annotations = tools["profile"].ProtocolTool.Annotations;
        Assert.Equal((false, false, true), (annotations?.ReadOnlyHint, annotations?.DestructiveHint, annotations?.IdempotentHint));
        var profile = typeof(ProfileTool).GetMethod(nameof(ProfileTool.Profile))!;
        Assert.Equal(["name", "signature", "formality", "language"], profile.GetParameters().Where(p => p.ParameterType.IsClientValue()).Select(p => p.Name));
        Assert.All(profile.GetParameters().Where(p => p.ParameterType.IsClientValue()), p => Assert.Equal(typeof(string), p.ParameterType));
        var description = tools["profile"].ProtocolTool.Description!;
        Assert.Contains("About me", description, StringComparison.Ordinal);
        Assert.Contains("Save only what the person asked you to save", description, StringComparison.Ordinal);
        Assert.Contains("never guess", description, StringComparison.Ordinal);
        Assert.Contains("Called without values it only reads", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Revise_order_asks_for_the_persons_words_unchanged_and_names_the_next_step()
    {
        var description = Tools()["revise_order"].ProtocolTool.Description!;

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
        var shipped = Provider();
        var shorter = Provider(maxWait: "25");

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
        var description = Description(Tools()["get_order_status"], "waitSeconds");

        Assert.DoesNotContain("50", description, StringComparison.Ordinal);
        Assert.Contains("description of this tool", description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_instructions_name_no_removed_tool()
    {
        var without = Instructions(Provider());
        Assert.All(Successors.Keys, name => Assert.DoesNotContain(name, without, StringComparison.Ordinal));
        Assert.DoesNotContain("older interface", without, StringComparison.Ordinal);
        Assert.EndsWith(McpServerInstructions.ProfileSavingLine, without, StringComparison.Ordinal);
    }

    [Fact]
    public void While_saving_is_off_profile_is_offered_read_only_with_its_earlier_description()
    {
        var profile = ToolsOf(Provider(tellOnce: TellOnceMode.Off))["profile"].ProtocolTool;

        Assert.Empty(profile.InputSchema.TryGetProperty("properties", out var properties) ? properties.EnumerateObject().Select(p => p.Name).ToList() : []);
        Assert.True(profile.Annotations?.ReadOnlyHint);
        Assert.Null(profile.Annotations?.DestructiveHint);
        Assert.Equal(ProfileTool.ReadOnlyToolDescription, profile.Description);
        Assert.Contains("Read only", profile.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TellOnceMode.On, true)]
    [InlineData(TellOnceMode.WithoutPromise, true)]
    [InlineData(TellOnceMode.Off, false)]
    public void The_instructions_offer_saving_about_me_details_only_while_the_switch_allows_it(TellOnceMode mode, bool saving)
    {
        var instructions = Instructions(Provider(tellOnce: mode));

        Assert.Equal(saving, instructions.Contains(McpServerInstructions.ProfileSavingLine, StringComparison.Ordinal));
        Assert.Equal(!saving, instructions.Contains(McpServerInstructions.ProfileReadOnlyLine, StringComparison.Ordinal));
        Assert.Equal(saving, instructions.Contains("aboutMeMissing", StringComparison.Ordinal));
        Assert.DoesNotContain("not leave this open again", instructions, StringComparison.Ordinal);
    }

    private static Dictionary<string, McpServerTool> Tools() => ToolsOf(Provider());

    private static Dictionary<string, McpServerTool> ToolsOf(ServiceProvider provider) =>
        provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.ToDictionary(t => t.ProtocolTool.Name, StringComparer.Ordinal);

    private static string Instructions(ServiceProvider provider) =>
        provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ServerInstructions!;

    private static ServiceProvider Provider(string? maxWait = null, TellOnceMode? tellOnce = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        if (maxWait is not null)
            services.Configure<OrderOptions>(o => o.MaxWaitSeconds = int.Parse(maxWait, System.Globalization.CultureInfo.InvariantCulture));
        if (tellOnce is { } mode)
            services.Configure<OrderOptions>(o => o.TellOnce = mode);
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
