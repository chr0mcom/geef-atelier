using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Mcp.Tools;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Mcp.Orders;
using ModelContextProtocol;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// Every MCP tool that reads or changes owned orders and runs refuses a caller without a user name who is no
/// administrator, with a message the client can show, before any data service is used (owner filter fails closed).
/// </summary>
public sealed class McpUnknownUserTests
{
    private static readonly string Id = Guid.NewGuid().ToString();

    public static TheoryData<string, string?> Cases()
    {
        var data = new TheoryData<string, string?>();
        foreach (var tool in Tools.Keys)
        {
            data.Add(tool, null);
            data.Add(tool, "");
            data.Add(tool, "  ");
        }
        return data;
    }

    private static readonly Dictionary<string, Func<ICurrentUserService, Task>> Tools = new()
    {
        ["get_order_status"] = user => GetOrderStatusTool.GetOrderStatus(NeverCalled<IOrderService>.Create(), user, OrderToolTestData.Options(), Id, 0),
        ["get_order_result"] = user => GetOrderResultTool.GetOrderResult(NeverCalled<IOrderService>.Create(), user, Id),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_caller_without_a_name_is_refused_before_any_data_is_read(string tool, string? username)
    {
        var ex = await Assert.ThrowsAsync<McpException>(() => Tools[tool](new FakeUser(username)));

        Assert.Equal(OwnerScope.UnknownUserMessage, ex.Message);
    }
}
