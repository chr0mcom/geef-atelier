using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Geef.Atelier.Mcp;

/// <summary>Stops the start of the host when a tool parameter that is a service has no registration (see <see cref="McpToolBinding"/>).</summary>
internal sealed class McpToolBindingCheck(IServiceProviderIsService services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var unbound = typeof(McpToolBinding).Assembly.ToolMethods().FindClientBoundServices(services);
        if (unbound.Count > 0)
            throw new InvalidOperationException(
                "These MCP tool parameters are services without a registration; the MCP SDK would read them from the client's arguments: "
                + string.Join(", ", unbound));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
