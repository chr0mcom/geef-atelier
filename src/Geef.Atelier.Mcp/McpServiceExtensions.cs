using System.Reflection;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Infrastructure.Orders;
using Geef.Atelier.Mcp.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp;

public static class McpServiceExtensions
{
    /// <summary>
    /// Registers the MCP server with the tools of <see cref="McpToolCatalog"/>: the order tools always, the ticket tools and the
    /// ticket step of the instructions while <c>Uploads:Tickets</c> is <c>On</c>, the older tools while <c>Mcp:LegacyTools</c> is true.
    /// While tickets are off, <c>submit_order</c> does not offer <c>uploadIds</c>, so the tool list and the instructions are the same
    /// as before upload tickets; while they are on, its description and that of <c>attachments</c> name <c>prepare_attachment</c>. The longest wait named in the instructions and in <c>get_order_status</c> is
    /// <c>Orders:MaxWaitSeconds</c>, the same value that limits the wait. Trace lines of the SDK, which hold whole messages with upload
    /// tokens and file content, are never written (<see cref="McpMessageLogGuard"/>). The transport is stateless on purpose
    /// (<see cref="HttpServerSessionMode.Stateless"/>): every request stands alone, for clients of the 2026-07-28 revision as for older ones.
    /// </summary>
    public static IServiceCollection AddAtelierMcp(this IServiceCollection services)
    {
        services.AddSingleton<IConfigureOptions<McpOptions>, McpOptionsFromConfiguration>();
        if (!services.Any(d => d.ServiceType == typeof(IOrderExportService)))
            services.AddOrderExport();

        services
            .AddMcpServer()
            .WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless);
        services.AddOptions<McpServerOptions>()
            .Configure<IOptions<OrderOptions>, IOptions<McpOptions>, IOptions<UploadOptions>, IEnumerable<TicketMcpTool>, IEnumerable<LegacyMcpTool>>(
                (o, orders, mcp, uploads, ticketTools, legacy) =>
            {
                var tickets = uploads.Value.Tickets == UploadTicketsMode.On;
                o.ServerInstructions = McpServerInstructions.For(orders.Value.MaxWaitSeconds, mcp.Value.LegacyTools, tickets, orders.Value.TellOnce != TellOnceMode.Off);
                if (tickets)
                {
                    o.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>();
                    foreach (var tool in ticketTools)
                        o.ToolCollection.Add(tool.Tool);
                }

                if (!mcp.Value.LegacyTools)
                    return;
                o.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>();
                foreach (var tool in legacy)
                    o.ToolCollection.Add(tool.Tool);
            });

        foreach (var method in McpToolCatalog.OrderTools.ToolMethods())
            services.AddSingleton(sp => CreateOrderTool(method, sp));
        foreach (var method in McpToolCatalog.TicketTools.ToolMethods())
            services.AddSingleton(sp => new TicketMcpTool(method.CreateTool(sp)));
        foreach (var method in McpToolCatalog.LegacyTools.ToolMethods())
            services.AddSingleton(sp => new LegacyMcpTool(method.CreateTool(sp)));

        services.AddHostedService<McpToolBindingCheck>();
        services.AddMcpMessageLogGuard();
        return services;
    }

    private static McpServerTool CreateOrderTool(MethodInfo method, IServiceProvider services)
    {
        var tickets = services.GetRequiredService<IOptions<UploadOptions>>().Value.Tickets == UploadTicketsMode.On;
        var submit = method.DeclaringType == typeof(SubmitOrderTool);
        var description = method.DeclaringType == typeof(GetOrderStatusTool)
            ? GetOrderStatusTool.Describe(services.GetRequiredService<IOptions<OrderOptions>>().Value.MaxWaitSeconds)
            : submit && tickets ? SubmitOrderTool.TicketToolDescription : null;
        if (method.DeclaringType == typeof(ProfileTool) && services.GetRequiredService<IOptions<OrderOptions>>().Value.TellOnce == TellOnceMode.Off)
            return ProfileTool.SavingParameters.Aggregate(method.CreateTool(services, ProfileTool.ReadOnlyToolDescription), (t, p) => t.WithoutInputProperty(p))
                .WithAnnotations(new ToolAnnotations { ReadOnlyHint = true });
        var tool = method.CreateTool(services, description);
        if (!submit)
            return tool;
        return tickets
            ? tool.WithInputPropertyDescription(SubmitOrderTool.AttachmentsParameter, SubmitOrderTool.TicketAttachmentsDescription)
            : tool.WithoutInputProperty(SubmitOrderTool.UploadIdsParameter);
    }

    private sealed class McpOptionsFromConfiguration(IServiceProvider services) : IConfigureOptions<McpOptions>
    {
        public void Configure(McpOptions options) =>
            services.GetService<IConfiguration>()?.GetSection(McpOptions.SectionName).Bind(options);
    }
}

/// <summary>An older tool; the server offers it only while <see cref="McpOptions.LegacyTools"/> is true.</summary>
internal sealed record LegacyMcpTool(McpServerTool Tool);

/// <summary>A tool of the upload ticket; the server offers it only while <see cref="UploadOptions.Tickets"/> is <c>On</c>.</summary>
internal sealed record TicketMcpTool(McpServerTool Tool);
