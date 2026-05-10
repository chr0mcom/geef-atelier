using System.ComponentModel.DataAnnotations;

namespace Geef.Atelier.Infrastructure.Llm;

public sealed class AnthropicOptions
{
    // TODO: Add .ValidateDataAnnotations().ValidateOnStart() in Program.cs when production key is available.
    [Required]
    public string ApiKey { get; init; } = string.Empty;

    public string ExecutorModel { get; init; } = "claude-opus-4-7";
    public string ReviewerModel { get; init; } = "claude-opus-4-7";
}
