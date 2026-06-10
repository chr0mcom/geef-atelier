using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Composition;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Parses the raw tool-call JSON string from a <c>submit_crew_spec</c> call into a <see cref="CrewSpecArtifact"/>.
/// All parsing is defensive: missing or malformed optional fields are silently replaced by their defaults.
/// </summary>
public static class CrewSpecParser
{
    /// <summary>
    /// Parses the raw JSON arguments produced by the <c>submit_crew_spec</c> tool call.
    /// </summary>
    /// <param name="toolCallArgumentsJson">Raw JSON string from the LLM tool-call arguments.</param>
    /// <returns>A fully populated <see cref="CrewSpecArtifact"/>; never throws on missing optional fields.</returns>
    public static CrewSpecArtifact Parse(string toolCallArgumentsJson)
    {
        using var doc = JsonDocument.Parse(toolCallArgumentsJson);
        var root = doc.RootElement;

        var mode = ParseMode(root.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() : null);

        var domain = root.TryGetProperty("domain", out var domainEl)
            ? domainEl.GetString() ?? "general"
            : "general";

        var rationale = root.TryGetProperty("rationale", out var rationaleEl)
            ? rationaleEl.GetString() ?? string.Empty
            : string.Empty;

        var existingTemplateName = root.TryGetProperty("existing_template_name", out var etnEl)
            ? etnEl.GetString()
            : null;

        CrewPartSpec? executor = null;
        if (root.TryGetProperty("executor", out var executorEl) && executorEl.ValueKind == JsonValueKind.Object)
            executor = ParsePartSpec(executorEl);

        var reviewers         = ParsePartSpecArray(root, "reviewers");
        var advisors          = ParsePartSpecArray(root, "advisors");
        var groundingProviders = ParsePartSpecArray(root, "grounding_providers");
        var finalizers        = ParsePartSpecArray(root, "finalizers");

        var evaluationStrategy = root.TryGetProperty("evaluation_strategy", out var esEl)
            ? esEl.GetString() ?? "Parallel"
            : "Parallel";

        int? maxIterations = null;
        if (root.TryGetProperty("max_iterations", out var miEl) && miEl.ValueKind == JsonValueKind.Number)
            maxIterations = miEl.GetInt32();

        bool? abortOnCritical = null;
        if (root.TryGetProperty("abort_on_critical", out var aocEl) &&
            (aocEl.ValueKind == JsonValueKind.True || aocEl.ValueKind == JsonValueKind.False))
            abortOnCritical = aocEl.GetBoolean();

        return new CrewSpecArtifact
        {
            Mode               = mode,
            Domain             = domain,
            Rationale          = rationale,
            ExistingTemplateName = existingTemplateName,
            Executor           = executor,
            Reviewers          = reviewers,
            Advisors           = advisors,
            GroundingProviders = groundingProviders,
            Finalizers         = finalizers,
            EvaluationStrategy = evaluationStrategy,
            MaxIterations      = maxIterations,
            AbortOnCritical    = abortOnCritical,
        };
    }

    private static CrewSpecMode ParseMode(string? raw) => raw?.ToLowerInvariant() switch
    {
        "existing-template" => CrewSpecMode.ExistingTemplate,
        "composed"          => CrewSpecMode.Composed,
        "new"               => CrewSpecMode.New,
        _                   => CrewSpecMode.New
    };

    private static IReadOnlyList<CrewPartSpec> ParsePartSpecArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arrEl) || arrEl.ValueKind != JsonValueKind.Array)
            return [];

        return arrEl.EnumerateArray()
            .Select(ParsePartSpec)
            .ToList();
    }

    private static CrewPartSpec ParsePartSpec(JsonElement el)
    {
        // If "reuse" is present, treat the entire element as a reuse-reference; ignore inline fields.
        if (el.TryGetProperty("reuse", out var reuseEl) && reuseEl.ValueKind == JsonValueKind.String)
        {
            return new CrewPartSpec { Reuse = reuseEl.GetString() };
        }

        return new CrewPartSpec
        {
            Name         = el.TryGetProperty("name",         out var nameEl)        ? nameEl.GetString()        : null,
            DisplayName  = el.TryGetProperty("display_name", out var dnEl)          ? dnEl.GetString()          : null,
            SystemPrompt = el.TryGetProperty("system_prompt", out var spEl)         ? spEl.GetString()          : null,
            Provider     = el.TryGetProperty("provider",     out var provEl)        ? provEl.GetString()        : null,
            Model        = el.TryGetProperty("model",        out var modelEl)       ? modelEl.GetString()       : null,
            MaxTokens    = el.TryGetProperty("max_tokens",   out var mtEl) && mtEl.ValueKind == JsonValueKind.Number
                               ? mtEl.GetInt32() : null,
            Priority     = el.TryGetProperty("priority",     out var priEl) && priEl.ValueKind == JsonValueKind.Number
                               ? priEl.GetInt32() : null,
            AdvisorMode    = el.TryGetProperty("advisor_mode",    out var amEl)     ? amEl.GetString()          : null,
            AdvisorTrigger = el.TryGetProperty("advisor_trigger", out var atEl)     ? atEl.GetString()          : null,
            ProviderType   = el.TryGetProperty("provider_type",   out var ptEl)     ? ptEl.GetString()          : null,
            FinalizerType  = el.TryGetProperty("finalizer_type",  out var ftEl)     ? ftEl.GetString()          : null,
            ToolNames      = el.TryGetProperty("tool_names", out var tnEl) && tnEl.ValueKind == JsonValueKind.Array
                                 ? tnEl.EnumerateArray()
                                       .Where(x => x.ValueKind == JsonValueKind.String)
                                       .Select(x => x.GetString()!)
                                       .ToList()
                                 : null,
        };
    }
}
