using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Defines the structured tool schema the crew-composer LLM must call to submit the composed
/// crew specification. The tool-call arguments JSON is returned verbatim as the pipeline artifact
/// and later validated / parsed by the crew-spec reviewer step.
/// </summary>
internal static class CrewSpecTool
{
    /// <summary>The name used both in the <c>ToolChoice</c> directive and to verify the LLM response.</summary>
    public const string ToolName = "submit_crew_spec";

    private const string SchemaJson = """
        {
            "type": "object",
            "required": ["mode", "reasoning"],
            "properties": {
                "mode": {
                    "type": "string",
                    "enum": ["existing-template", "composed", "new"],
                    "description": "How the crew is specified. 'existing-template' reuses an existing named template; 'composed' assembles existing profiles; 'new' proposes new profiles."
                },
                "reasoning": {
                    "type": "string",
                    "description": "Concise explanation of why this crew configuration was chosen for the task."
                },
                "existing_template_name": {
                    "type": "string",
                    "description": "Name of the existing crew template to reuse. Required when mode='existing-template'."
                },
                "executor_profile_name": {
                    "type": "string",
                    "description": "Name of the executor profile. Required when mode='composed' or mode='new'."
                },
                "reviewer_profile_names": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Names of reviewer profiles in execution order. At least one required when mode='composed' or mode='new'."
                },
                "advisor_profile_names": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Names of advisor profiles. Optional."
                },
                "grounding_provider_names": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Names of grounding-provider profiles to run before execution. Optional."
                },
                "finalizer_profile_names": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "Names of finalizer profiles to run after convergence. Optional."
                },
                "evaluation_strategy": {
                    "type": "string",
                    "enum": ["Sequential", "Parallel", "FailFast", "Priority"],
                    "description": "Reviewer evaluation strategy. Defaults to 'Sequential' when omitted."
                },
                "proposed_new_profiles": {
                    "type": "array",
                    "description": "New profiles to be created. Only used when mode='new'. Each entry must have complete, task-specific prompts.",
                    "items": {
                        "type": "object",
                        "required": ["profile_type", "name", "display_name", "description", "provider", "model", "system_prompt"],
                        "properties": {
                            "profile_type": {
                                "type": "string",
                                "enum": ["executor", "reviewer", "advisor", "finalizer", "grounding_provider"]
                            },
                            "name": { "type": "string" },
                            "display_name": { "type": "string" },
                            "description": { "type": "string" },
                            "provider": { "type": "string" },
                            "model": { "type": "string" },
                            "system_prompt": {
                                "type": "string",
                                "description": "Full system prompt. Reviewer prompts MUST include the severity taxonomy block (critical/major/minor/info)."
                            },
                            "max_tokens": { "type": "integer" },
                            "reviewer_focus": { "type": "string" },
                            "advisor_mode": {
                                "type": "string",
                                "enum": ["Strategic", "Critical", "DevilsAdvocate", "DomainExpert"]
                            },
                            "advisor_trigger": {
                                "type": "string",
                                "enum": ["BeforeFirstExecution", "BeforeEveryExecution", "OnConvergenceFailure"]
                            },
                            "finalizer_type": {
                                "type": "string",
                                "enum": ["FileExport", "MetadataEnrich", "ExternalSink", "Transform"]
                            },
                            "finalizer_settings": { "type": "object" },
                            "grounding_provider_type": { "type": "string" },
                            "grounding_provider_settings": { "type": "object" }
                        }
                    }
                }
            }
        }
        """;

    /// <summary>The <see cref="LlmTool"/> definition passed to the LLM request.</summary>
    public static readonly LlmTool Schema = new()
    {
        Name        = ToolName,
        Description = "Submit a complete crew specification for the given task. Call this tool exactly once with the full crew configuration.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>(SchemaJson)
    };
}
