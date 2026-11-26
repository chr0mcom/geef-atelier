using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Mcp;

/// <summary>
/// The MCP SDK writes every JSON-RPC message it sends or receives, in full, at <see cref="LogLevel.Trace"/>: one-time upload tokens in the
/// answer of <c>prepare_attachment</c>, order texts and Base64 file content in the arguments. This guard drops trace lines of the SDK's
/// categories whatever the logging configuration says; every other line keeps the rule it had.
/// </summary>
public static class McpMessageLogGuard
{
    /// <summary>Prefix of the log categories of the MCP SDK.</summary>
    public const string CategoryPrefix = "ModelContextProtocol";

    /// <summary>
    /// Adds the guard after every other logging configuration: each filter rule keeps its provider, category and level and gets the
    /// guard in front of its own filter; a first catch-all rule at the configured minimum level covers categories no rule matches.
    /// </summary>
    public static IServiceCollection AddMcpMessageLogGuard(this IServiceCollection services) =>
        services.PostConfigure<LoggerFilterOptions>(options =>
        {
            var rules = options.Rules.ToList();
            options.Rules.Clear();
            options.Rules.Add(new LoggerFilterRule(null, null, options.MinLevel, Guarded(null)));
            foreach (var rule in rules)
                options.Rules.Add(new LoggerFilterRule(rule.ProviderName, rule.CategoryName, rule.LogLevel, Guarded(rule.Filter)));
        });

    /// <summary>Whether a line of <paramref name="category"/> at <paramref name="level"/> may be written at all.</summary>
    public static bool Allows(string? category, LogLevel level) =>
        level > LogLevel.Trace || category is null || !category.StartsWith(CategoryPrefix, StringComparison.Ordinal);

    private static Func<string?, string?, LogLevel, bool> Guarded(Func<string?, string?, LogLevel, bool>? inner) =>
        (provider, category, level) => Allows(category, level) && (inner?.Invoke(provider, category, level) ?? true);
}
