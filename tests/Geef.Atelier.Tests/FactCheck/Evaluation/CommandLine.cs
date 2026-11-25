using System.Globalization;

namespace Geef.Atelier.Tools.FactCheckEval;

/// <summary>A wrong command line; the message names the option.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// The options of one command: <c>--name value</c> pairs, a name may repeat (all values kept). Only the names a command
/// declares are accepted, so a secret can never be passed as an argument by mistake.
/// </summary>
public sealed class CommandLine
{
    /// <summary>Environment variable with the gateway key; the tool never takes it as an argument and never prints it.</summary>
    public const string ApiKeyVariable = "FACTCHECK_EVAL_API_KEY";

    private readonly Dictionary<string, List<string>> _values;

    private CommandLine(Dictionary<string, List<string>> values) => _values = values;

    /// <summary>Reads <paramref name="args"/> (without the command name); throws <see cref="UsageException"/> on an unknown or valueless option.</summary>
    public static CommandLine Parse(IReadOnlyList<string> args, IReadOnlyCollection<string> allowed)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                throw new UsageException($"option {args[i]} needs a value");
            var name = args[i][2..];
            if (!allowed.Contains(name))
                throw new UsageException($"unknown option --{name}");
            if (!values.TryGetValue(name, out var list))
                values[name] = list = [];
            list.Add(args[i + 1]);
        }
        return new CommandLine(values);
    }

    /// <summary>The single value of a required option.</summary>
    public string Required(string name) => Optional(name) ?? throw new UsageException($"--{name} is required");

    /// <summary>The single value of an option, or null.</summary>
    public string? Optional(string name)
    {
        if (!_values.TryGetValue(name, out var list))
            return null;
        if (list.Count > 1)
            throw new UsageException($"--{name} is given more than once");
        return list[0];
    }

    /// <summary>Every value of a repeatable option.</summary>
    public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out var list) ? list : [];

    /// <summary>A whole number in <c>[min, max]</c>; <paramref name="fallback"/> when the option is missing, required when it is null.</summary>
    public int Number(string name, int min, int max, int? fallback = null)
    {
        var text = fallback is null ? Required(name) : Optional(name);
        if (text is null)
            return fallback!.Value;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
            throw new UsageException($"--{name} must be a whole number from {min} to {max}");
        return value;
    }

    /// <summary>The gateway key from <see cref="ApiKeyVariable"/>, or null when it is not set.</summary>
    public static string? ApiKey(Func<string, string?> environment) =>
        environment(ApiKeyVariable) is { Length: > 0 } key ? key : null;
}
