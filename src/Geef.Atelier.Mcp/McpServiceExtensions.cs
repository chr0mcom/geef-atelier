using System.Reflection;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Infrastructure.Orders;
using Geef.Atelier.Mcp.Forms;
using Geef.Atelier.Mcp.Tools;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp;

public static class McpServiceExtensions
{
    /// <summary>
    /// Registers the MCP server with the tools of <see cref="McpToolCatalog"/>: the order tools and the stubs of the removed older tools
    /// always, the ticket tools and the ticket step of the instructions while <c>Uploads:Tickets</c> is <c>On</c>.
    /// While tickets are off, <c>submit_order</c> does not offer <c>uploadIds</c>, so the tool list and the instructions are the same
    /// as before upload tickets; while they are on, its description and that of <c>attachments</c> name <c>prepare_attachment</c>. The longest wait named in the instructions and in <c>get_order_status</c> is
    /// <c>Orders:MaxWaitSeconds</c>, the same value that limits the wait. Trace lines of the SDK, which hold whole messages with upload
    /// tokens and file content, are never written (<see cref="McpMessageLogGuard"/>). The transport is stateless on purpose
    /// (<see cref="HttpServerSessionMode.Stateless"/>): every request stands alone, for clients of the 2026-07-28 revision as for older ones.
    /// <c>submit_order</c> offers <c>extraCareful</c> only while "Extra careful" is offered to anybody (<c>Orders:CarefulOutline</c>, D-091); a call
    /// with it from a person it is not offered to is refused with a plain sentence. While <c>Mcp:Skills</c> is <c>On</c> the server also offers <see cref="AtelierSkill"/>; while <c>Mcp:Elicitation</c> is <c>On</c>,
    /// <c>submit_order</c> may ask its questions as a form of the client (<see cref="IMcpOrderForms"/>). Both are off by default, and then tool list,
    /// instructions and capabilities are the same as without them (D-088).
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
            .Configure<IOptions<OrderOptions>, IOptions<McpOptions>, IOptions<UploadOptions>, IEnumerable<TicketMcpTool>>(
                (o, orders, mcp, uploads, ticketTools) =>
            {
                var tickets = uploads.Value.Tickets == UploadTicketsMode.On;
                o.ServerInstructions = McpServerInstructions.For(orders.Value.MaxWaitSeconds, tickets, orders.Value.TellOnce != TellOnceMode.Off);
                if (mcp.Value.Skills == McpSkillsMode.On)
                    AtelierSkill.AddTo(o);
                if (!tickets)
                    return;
                o.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>();
                foreach (var tool in ticketTools)
                    o.ToolCollection.Add(tool.Tool);
            });

        foreach (var method in McpToolCatalog.OrderTools.ToolMethods())
            services.AddSingleton(sp => CreateOrderTool(method, sp));
        foreach (var method in McpToolCatalog.TicketTools.ToolMethods())
            services.AddSingleton(sp => new TicketMcpTool(method.CreateTool(sp)));
        foreach (var method in McpToolCatalog.RetiredTools.ToolMethods())
            services.AddSingleton(sp => method.CreateTool(sp));

        services.AddDataProtection();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IOrderFormStates, OrderFormStates>();
        services.AddScoped<IMcpOrderForms, McpOrderForms>();

        services.AddHostedService<McpToolBindingCheck>();
        services.AddMcpMessageLogGuard();
        return services;
    }

    private static McpServerTool CreateOrderTool(MethodInfo method, IServiceProvider services)
    {
        var tickets = services.GetRequiredService<IOptions<UploadOptions>>().Value.Tickets == UploadTicketsMode.On;
        var orders = services.GetRequiredService<IOptions<OrderOptions>>().Value;
        var careful = orders.OffersCarefulToAnyone();
        var submit = method.DeclaringType == typeof(SubmitOrderTool);
        var description = method.DeclaringType == typeof(GetOrderStatusTool)
            ? GetOrderStatusTool.Describe(orders.MaxWaitSeconds)
            : submit ? SubmitOrderTool.Describe(tickets, services.GetRequiredService<IOptions<McpOptions>>().Value, careful) : null;
        if (method.DeclaringType == typeof(ProfileTool) && orders.TellOnce == TellOnceMode.Off)
            return ProfileTool.SavingParameters.Aggregate(method.CreateTool(services, ProfileTool.ReadOnlyToolDescription), (t, p) => t.WithoutInputProperty(p))
                .WithAnnotations(new ToolAnnotations { ReadOnlyHint = true });
        var tool = method.CreateTool(services, description);
        if (!submit)
            return tool;
        if (!careful)
            tool = tool.WithoutInputProperty(SubmitOrderTool.ExtraCarefulParameter);
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

/// <summary>A tool of the upload ticket; the server offers it only while <see cref="UploadOptions.Tickets"/> is <c>On</c>.</summary>
internal sealed record TicketMcpTool(McpServerTool Tool);
