using System.Reflection;
using System.Text.Json;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Mcp;
using Geef.Atelier.Mcp.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// The MCP SDK lists a tool parameter as client argument when the provider does not know its type as a service.
/// <see cref="McpToolBinding"/> finds such parameters, and <c>McpToolBindingCheck</c> refuses to start the host with them.
/// </summary>
public sealed class McpToolBindingTests
{
    [Fact]
    public void The_sdk_lists_an_unregistered_service_as_client_argument_and_the_check_names_it()
    {
        var method = typeof(SampleTools).GetMethod(nameof(SampleTools.Echo))!;

        using var without = new ServiceCollection().BuildServiceProvider();
        var listed = McpServerTool.Create(method, options: new McpServerToolCreateOptions { Services = without });
        Assert.Contains("currentUser", Names(listed.ProtocolTool.InputSchema));
        Assert.Contains("settings", Names(listed.ProtocolTool.InputSchema));
        Assert.Equal(
            ["SampleTools.Echo(currentUser: ICurrentUserService)", "SampleTools.Echo(settings: SampleSettings)"],
            new[] { method }.FindClientBoundServices(without.GetRequiredService<IServiceProviderIsService>()));

        using var with = new ServiceCollection()
            .AddScoped<ICurrentUserService, SampleUser>()
            .AddSingleton<SampleSettings>()
            .BuildServiceProvider();
        var bound = McpServerTool.Create(method, options: new McpServerToolCreateOptions { Services = with });
        Assert.Equal(["text"], Names(bound.ProtocolTool.InputSchema));
        Assert.Empty(new[] { method }.FindClientBoundServices(with.GetRequiredService<IServiceProviderIsService>()));
    }

    [Fact]
    public async Task The_host_does_not_start_when_a_tool_service_has_no_registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAtelierMcp();
        await using var provider = services.BuildServiceProvider();

        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(McpToolBindingCheck));
        var check = ActivatorUtilities.CreateInstance<McpToolBindingCheck>(provider);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => check.StartAsync(CancellationToken.None));

        Assert.Contains("SubmitOrderTool.SubmitOrder(currentUser: ICurrentUserService)", error.Message);
        Assert.Contains("AnswerOrderTool.AnswerOrder(orders: IOrderService)", error.Message);
    }

    [Fact]
    public void Every_parameter_of_every_tool_is_a_client_value_an_sdk_value_or_an_interface_or_options_service()
    {
        var parameters = typeof(McpAssemblyMarker).Assembly.ToolMethods().SelectMany(m => m.GetParameters()).ToList();

        Assert.NotEmpty(parameters);
        Assert.All(
            parameters.Where(p => !p.ParameterType.IsClientValue() && !p.ParameterType.IsSdkValue()),
            p => Assert.True(p.ParameterType.IsInterface, $"{p.Member.DeclaringType?.Name}.{p.Member.Name}({p.Name}) is a {p.ParameterType.Name}."));
    }

    [Theory]
    [InlineData(typeof(string), true)]
    [InlineData(typeof(int?), true)]
    [InlineData(typeof(Guid), true)]
    [InlineData(typeof(DayOfWeek), true)]
    [InlineData(typeof(IReadOnlyList<AnswerInputDto>), true)]
    [InlineData(typeof(AnswerInputDto[]), true)]
    [InlineData(typeof(ICurrentUserService), false)]
    [InlineData(typeof(SampleSettings), false)]
    [InlineData(typeof(IReadOnlyList<SampleSettings>), false)]
    [InlineData(typeof(object), false)]
    [InlineData(typeof(JsonElement), false)]
    public void Client_values_are_scalars_enums_and_the_wire_records(Type type, bool expected) =>
        Assert.Equal(expected, type.IsClientValue());

    private static IReadOnlyList<string> Names(JsonElement schema) =>
        schema.TryGetProperty("properties", out var properties) ? properties.EnumerateObject().Select(p => p.Name).ToList() : [];

    /// <summary>A concrete class a client could fill field by field if the SDK took it for an argument.</summary>
    public sealed class SampleSettings
    {
        public bool IsAdmin { get; set; }
    }

    private sealed class SampleUser : ICurrentUserService
    {
        public string? Username => "alice";

        public bool IsAuthenticated => true;

        public bool IsAdmin => false;
    }

    private static class SampleTools
    {
        public static string Echo(ICurrentUserService currentUser, SampleSettings settings, string text, CancellationToken cancellationToken = default) =>
            $"{currentUser.Username}:{settings.IsAdmin}:{text}";
    }
}
