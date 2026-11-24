namespace Geef.Atelier.Application.Security;

/// <summary>Raised when a configuration field names an environment variable that is not on the allow list.</summary>
public sealed class EnvReferenceNotAllowedException(string field, string name)
    : InvalidOperationException(
        $"{field}: the environment variable '{name}' is not allowed. Use a name from the allow list " +
        "(for example OPENAI_API_KEY or TAVILY_API_KEY) or one starting with ATELIER_SECRET_. " +
        "An administrator can extend the list under EnvReference:Allowed.")
{
    /// <summary>The rejected field.</summary>
    public string Field { get; } = field;

    /// <summary>The rejected environment-variable name.</summary>
    public string Name { get; } = name;
}
