using System.Reflection;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp;

/// <summary>
/// Which tool parameters a client may fill. The MCP SDK decides once, when it creates a tool, whether a parameter is a
/// service: only when the root provider reports it as registered (<see cref="IServiceProviderIsService"/>). Every other
/// parameter appears in <c>tools/list</c> and is read from the arguments of the call. A service whose registration is
/// missing would therefore be filled by the client; this check refuses such a wiring before the server starts.
/// </summary>
public static class McpToolBinding
{
    private static readonly HashSet<Type> ScalarTypes =
    [
        typeof(string), typeof(bool), typeof(byte), typeof(short), typeof(int), typeof(long), typeof(float), typeof(double),
        typeof(decimal), typeof(Guid), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan),
    ];

    private static readonly HashSet<Type> SdkTypes =
    [
        typeof(CancellationToken), typeof(IServiceProvider), typeof(McpServer), typeof(RequestContext<CallToolRequestParams>),
        typeof(IProgress<ProgressNotificationValue>), typeof(ClaimsPrincipal),
    ];

    private static readonly Type[] SequenceTypes =
    [
        typeof(IEnumerable<>), typeof(IReadOnlyList<>), typeof(IReadOnlyCollection<>), typeof(IList<>), typeof(ICollection<>), typeof(List<>),
    ];

    /// <summary>The tool methods <c>WithToolsFromAssembly</c> registers: methods with <see cref="McpServerToolAttribute"/> on types with <see cref="McpServerToolTypeAttribute"/>.</summary>
    public static IReadOnlyList<MethodInfo> ToolMethods(this Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .ToList();

    /// <summary>
    /// True for values a client may send: scalars, enums, the wire records of this assembly (names ending in <c>Dto</c>),
    /// nullable forms and sequences of them. Everything else is a service or an SDK value and must never come from the client.
    /// </summary>
    public static bool IsClientValue(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (Nullable.GetUnderlyingType(type) is { } inner)
            return inner.IsClientValue();
        if (ScalarTypes.Contains(type) || type.IsEnum)
            return true;
        if (type.IsArray)
            return type.GetElementType()!.IsClientValue();
        if (type.IsGenericType && SequenceTypes.Contains(type.GetGenericTypeDefinition()))
            return type.GetGenericArguments()[0].IsClientValue();
        return type.Assembly == typeof(McpToolBinding).Assembly && type.Name.EndsWith("Dto", StringComparison.Ordinal);
    }

    /// <summary>True for parameters the SDK fills itself (cancellation, request context, server, progress, user).</summary>
    public static bool IsSdkValue(this Type type) => SdkTypes.Contains(type);

    /// <summary>
    /// Parameters that are neither client values nor SDK values and that <paramref name="services"/> does not know as a
    /// service, as <c>Type.Method(parameter: Type)</c>. The SDK would read each of them from the arguments of the client.
    /// </summary>
    public static IReadOnlyList<string> FindClientBoundServices(this IEnumerable<MethodInfo> tools, IServiceProviderIsService services)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(services);
        return tools
            .SelectMany(m => m.GetParameters().Select(p => (Method: m, Parameter: p)))
            .Where(x => !x.Parameter.ParameterType.IsClientValue()
                        && !x.Parameter.ParameterType.IsSdkValue()
                        && !services.IsService(x.Parameter.ParameterType))
            .Select(x => $"{x.Method.DeclaringType?.Name}.{x.Method.Name}({x.Parameter.Name}: {x.Parameter.ParameterType.Name})")
            .ToList();
    }
}
