using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.TemplateStudio;

/// <summary>
/// Defines the structured tool schema the Template Studio meta-LLM must use to submit its analysis.
/// Mirrors the OpenAI tool-use pattern used by <c>ReviewerToolDefinition</c>.
/// </summary>
internal static class TemplateProposalTool
{
    public const string ToolName = "submit_template_proposal";

    private const string SchemaJson = """
        {
            "type": "object",
            "required": ["matched_existing_templates", "recommendation", "reasoning_summary"],
            "properties": {
                "matched_existing_templates": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "required": ["template_name", "confidence", "reasoning"],
                        "properties": {
                            "template_name": { "type": "string" },
                            "confidence": { "type": "number", "minimum": 0, "maximum": 1 },
                            "reasoning": { "type": "string" }
                        }
                    }
                },
                "recommendation": {
                    "type": "string",
                    "enum": ["use_existing", "create_new", "adapt_existing"]
                },
                "proposed_template": {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string" },
                        "display_name": { "type": "string" },
                        "description": { "type": "string" },
                        "executor_profile_name": { "type": "string" },
                        "reviewer_profile_names": { "type": "array", "items": { "type": "string" } },
                        "advisor_profile_names": { "type": "array", "items": { "type": "string" } },
                        "grounding_provider_profile_names": { "type": "array", "items": { "type": "string" } },
                        "finalizer_profile_names": { "type": "array", "items": { "type": "string" } },
                        "run_finalizers_on_max_attempts": { "type": "boolean" },
                        "evaluation_strategy": {
                            "type": "string",
                            "enum": ["Sequential", "Parallel", "FailFast", "Priority"]
                        },
                        "evaluation_strategy_reasoning": { "type": "string" },
                        "finalizer_reasoning": { "type": "string" }
                    }
                },
                "proposed_new_profiles": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "required": ["profile_type", "name", "display_name", "description", "model", "provider", "system_prompt"],
                        "properties": {
                            "profile_type": { "type": "string", "enum": ["reviewer", "advisor", "grounding_provider", "executor", "finalizer"] },
                            "name": { "type": "string" },
                            "display_name": { "type": "string" },
                            "description": { "type": "string" },
                            "model": { "type": "string" },
                            "provider": { "type": "string" },
                            "system_prompt": { "type": "string" },
                            "max_tokens": { "type": "integer" },
                            "reviewer_focus": {
                                "type": "string",
                                "description": "Concise focus hint for the reviewer, e.g. 'legal clause risk' or 'argument strength'. Required for reviewer profiles."
                            },
                            "advisor_mode": {
                                "type": "string",
                                "enum": ["Strategic", "Critical", "DevilsAdvocate", "DomainExpert"]
                            },
                            "advisor_trigger": {
                                "type": "string",
                                "enum": ["BeforeFirstExecution", "BeforeEveryExecution", "OnConvergenceFailure"]
                            },
                            "grounding_provider_type": { "type": "string" },
                            "grounding_provider_settings": { "type": "object" },
                            "finalizer_type": {
                                "type": "string",
                                "enum": ["FileExport", "MetadataEnrich", "ExternalSink", "Transform"]
                            },
                            "finalizer_settings": { "type": "object" },
                            "model_reasoning": { "type": "string" },
                            "system_prompt_reasoning": { "type": "string" },
                            "overall_reasoning": { "type": "string" },
                            "mode_reasoning": { "type": "string" },
                            "trigger_reasoning": { "type": "string" },
                            "finalizer_reasoning": { "type": "string" }
                        }
                    }
                },
                "reasoning_summary": { "type": "string" }
            }
        }
        """;

    public static readonly LlmTool Schema = new()
    {
        Name        = ToolName,
        Description = "Submit a structured analysis of the task description and propose a crew template configuration.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>(SchemaJson)
    };
}
