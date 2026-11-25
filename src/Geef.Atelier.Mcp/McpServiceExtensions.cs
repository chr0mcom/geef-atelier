using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Infrastructure.Orders;
using Geef.Atelier.Mcp.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp;

public static class McpServiceExtensions
{
    /// <summary>
    /// Registers the MCP server with the tools of <see cref="McpToolCatalog"/>: the order tools always, the older tools while
    /// <c>Mcp:LegacyTools</c> is true. The longest wait named in the instructions and in <c>get_order_status</c> is
    /// <c>Orders:MaxWaitSeconds</c>, the same value that limits the wait.
    /// </summary>
    public static IServiceCollection AddAtelierMcp(this IServiceCollection services)
    {
        services.AddSingleton<IConfigureOptions<McpOptions>, McpOptionsFromConfiguration>();
        if (!services.Any(d => d.ServiceType == typeof(IOrderExportService)))
            services.AddOrderExport();

        services
            .AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true);
        services.AddOptions<McpServerOptions>()
            .Configure<IOptions<OrderOptions>, IOptions<McpOptions>, IEnumerable<LegacyMcpTool>>((o, orders, mcp, legacy) =>
            {
                o.ServerInstructions = McpServerInstructions.For(orders.Value.MaxWaitSeconds, mcp.Value.LegacyTools);
                if (!mcp.Value.LegacyTools)
                    return;
                o.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>();
                foreach (var tool in legacy)
                    o.ToolCollection.Add(tool.Tool);
            });

        foreach (var method in McpToolCatalog.OrderTools.ToolMethods())
            services.AddSingleton(sp => method.CreateTool(sp, method.DeclaringType == typeof(GetOrderStatusTool)
                ? GetOrderStatusTool.Describe(sp.GetRequiredService<IOptions<OrderOptions>>().Value.MaxWaitSeconds)
                : null));
        foreach (var method in McpToolCatalog.LegacyTools.ToolMethods())
            services.AddSingleton(sp => new LegacyMcpTool(method.CreateTool(sp)));

        services.AddHostedService<McpToolBindingCheck>();
        return services;
    }

    private sealed class McpOptionsFromConfiguration(IServiceProvider services) : IConfigureOptions<McpOptions>
    {
        public void Configure(McpOptions options) =>
            services.GetService<IConfiguration>()?.GetSection(McpOptions.SectionName).Bind(options);
    }
}

/// <summary>An older tool; the server offers it only while <see cref="McpOptions.LegacyTools"/> is true.</summary>
internal sealed record LegacyMcpTool(McpServerTool Tool);
