using System.Reflection;
using System.Text.Json;
using Geef.Atelier.Mcp;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Services of an MCP tool are bound from the server's DI, never from the client's arguments. The SDK lists a parameter as a
/// client argument when the root provider does not know its type as a service; these tests check the production wiring
/// (<c>Program</c>) over the real <c>/mcp</c> endpoint: <c>tools/list</c> of every tool and a call that sends a forged identity.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class McpToolBindingEndToEndTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Tools_list_shows_exactly_the_client_values_of_every_tool_and_no_service()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);

        var response = await host.PostMcpAsync("tools/list");
        var listed = response.GetProperty("result").GetProperty("tools").EnumerateArray()
            .ToDictionary(t => t.GetProperty("name").GetString()!, t => Properties(t.GetProperty("inputSchema")));
        var methods = typeof(McpAssemblyMarker).Assembly.ToolMethods().ToDictionary(ToolName);

        Assert.Equal(methods.Keys.Order(StringComparer.Ordinal), listed.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, method) in methods)
        {
            var parameters = method.GetParameters();
            var expected = parameters.Where(p => p.ParameterType.IsClientValue()).Select(p => p.Name!).Order(StringComparer.Ordinal).ToList();
            Assert.True(expected.SequenceEqual(listed[name].Order(StringComparer.Ordinal)),
                $"{name}: listed [{string.Join(", ", listed[name])}], expected [{string.Join(", ", expected)}].");

            var services = parameters.Where(p => !p.ParameterType.IsClientValue()).Select(p => p.Name!).ToList();
            Assert.False(services.Intersect(listed[name]).Any(), $"{name} lists a service as client argument: {string.Join(", ", services.Intersect(listed[name]))}.");
        }

        Assert.Contains("currentUser", methods.Values.SelectMany(m => m.GetParameters()).Select(p => p.Name));
        Assert.Contains("studioService", methods.Values.SelectMany(m => m.GetParameters()).Select(p => p.Name));
    }

    [Fact]
    public async Task A_forged_current_user_in_the_arguments_does_not_change_who_submits_the_order()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        host.Script.ExecutorText = string.Join(' ', Enumerable.Repeat("Hallo", 150));

        var forged = new { username = "someone-else", requestingUsername = "someone-else", isAdmin = true, isAuthenticated = true };
        var submitted = await host.CallMcpToolAsync("submit_order", new
        {
            request = "Schreib eine kurze Mail an das Team: Das Sommerfest fällt aus.",
            currentUser = forged,
            orders = new { },
            intakeOptions = new { submitWaitSeconds = 0 },
        });
        host.ReleaseOrchestrator();

        var orderId = Guid.Parse(submitted.GetProperty("orderId").GetString()!);
        var owner = await host.QueryAsync(db => db.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.CreatedByUser).SingleAsync());
        Assert.Equal(AtelierEndToEndHost.McpTokenUser, owner);

        var status = await host.CallMcpToolAsync("get_order_status", new { orderId = orderId.ToString(), waitSeconds = 0, currentUser = forged });
        Assert.Equal(orderId.ToString(), status.GetProperty("orderId").GetString());

        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId));
        Assert.Equal(AtelierEndToEndHost.McpTokenUser, run.CreatedByUser);
    }

    private static IReadOnlyList<string> Properties(JsonElement schema) =>
        schema.TryGetProperty("properties", out var properties) ? properties.EnumerateObject().Select(p => p.Name).ToList() : [];

    private static string ToolName(MethodInfo method)
    {
        var name = method.GetCustomAttribute<McpServerToolAttribute>()?.Name;
        if (name is not null)
            return name;
        var stem = method.Name.EndsWith("Async", StringComparison.Ordinal) ? method.Name[..^"Async".Length] : method.Name;
        return JsonNamingPolicy.SnakeCaseLower.ConvertName(stem);
    }
}
