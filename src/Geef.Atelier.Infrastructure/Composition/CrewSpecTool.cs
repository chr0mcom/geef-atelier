using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Defines the structured tool schema the composition meta-LLM must use to submit its crew specification.
/// Mirrors the pattern established by <c>TemplateProposalTool</c>.
/// </summary>
internal static class CrewSpecTool
{
    /// <summary>The tool name used in LLM tool-choice requests.</summary>
    public const string ToolName = "submit_crew_spec";

    private const string SchemaJson = """
        {
            "type": "object",
            "required": ["mode", "domain", "rationale"],
            "properties": {
                "mode": {
                    "type": "string",
                    "enum": ["existing-template", "composed", "new"]
                },
                "domain": {
                    "type": "string"
                },
                "rationale": {
                    "type": "string"
                },
                "existing_template_name": {
                    "type": "string"
                },
                "executor": {
                    "type": "object",
                    "properties": {
                        "reuse": { "type": "string" },
                        "name": { "type": "string" },
                        "display_name": { "type": "string" },
                        "system_prompt": { "type": "string" },
                        "provider": { "type": "string" },
                        "model": { "type": "string" },
                        "max_tokens": { "type": "integer" },
                        "priority": { "type": "integer" },
                        "advisor_mode": {
                            "type": "string",
                            "enum": ["Strategic", "Critical", "DevilsAdvocate", "DomainExpert"]
                        },
                        "advisor_trigger": {
                            "type": "string",
                            "enum": ["BeforeFirstExecution", "BeforeEveryExecution", "OnConvergenceFailure"]
                        },
                        "provider_type": { "type": "string" },
                        "finalizer_type": { "type": "string" },
                        "tool_names": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "Optional tool names from the tool catalogue to bind to this actor."
                        }
                    }
                },
                "reviewers": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "reuse": { "type": "string" },
                            "name": { "type": "string" },
                            "display_name": { "type": "string" },
                            "system_prompt": { "type": "string" },
                            "provider": { "type": "string" },
                            "model": { "type": "string" },
                            "max_tokens": { "type": "integer" },
                            "priority": { "type": "integer" },
                            "advisor_mode": {
                                "type": "string",
                                "enum": ["Strategic", "Critical", "DevilsAdvocate", "DomainExpert"]
                            },
                            "advisor_trigger": {
                                "type": "string",
                                "enum": ["BeforeFirstExecution", "BeforeEveryExecution", "OnConvergenceFailure"]
                            },
                            "provider_type": { "type": "string" },
                            "finalizer_type": { "type": "string" },
                            "tool_names": {
                                "type": "array",
                                "items": { "type": "string" },
                                "description": "Optional tool names from the tool catalogue to bind to this actor."
                            }
                        }
                    }
                },
                "advisors": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "reuse": { "type": "string" },
                            "name": { "type": "string" },
                            "display_name": { "type": "string" },
                            "system_prompt": { "type": "string" },
                            "provider": { "type": "string" },
                            "model": { "type": "string" },
                            "max_tokens": { "type": "integer" },
                            "priority": { "type": "integer" },
                            "advisor_mode": {
                                "type": "string",
                                "enum": ["Strategic", "Critical", "DevilsAdvocate", "DomainExpert"]
                            },
                            "advisor_trigger": {
                                "type": "string",
                                "enum": ["BeforeFirstExecution", "BeforeEveryExecution", "OnConvergenceFailure"]
                            },
                            "provider_type": { "type": "string" },
                            "finalizer_type": { "type": "string" },
                            "tool_names": {
                                "type": "array",
                                "items": { "type": "string" },
                                "description": "Optional tool names from the tool catalogue to bind to this actor."
                            }
                        }
                    }
                },
                "grounding_providers": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "reuse": { "type": "string" },
                            "name": { "type": "string" },
                            "display_name": { "type": "string" },
                            "system_prompt": { "type": "string" },
                            "provider": { "type": "string" },
                            "model": { "type": "string" },
                            "max_tokens": { "type": "integer" },
                            "priority": { "type": "integer" },
                            "advisor_mode": {
                                "type": "string",
                                "enum": ["Strategic", "Critical", "DevilsAdvocate", "DomainExpert"]
                            },
                            "advisor_trigger": {
                                "type": "string",
                                "enum": ["BeforeFirstExecution", "BeforeEveryExecution", "OnConvergenceFailure"]
                            },
                            "provider_type": { "type": "string" },
                            "finalizer_type": { "type": "string" },
                            "tool_names": {
                                "type": "array",
                                "items": { "type": "string" },
                                "description": "Optional tool names from the tool catalogue to bind to this actor."
                            }
                        }
                    }
                },
                "finalizers": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "reuse": { "type": "string" },
                            "name": { "type": "string" },
                            "display_name": { "type": "string" },
                            "system_prompt": { "type": "string" },
                            "provider": { "type": "string" },
                            "model": { "type": "string" },
                            "max_tokens": { "type": "integer" },
                            "priority": { "type": "integer" },
                            "advisor_mode": {
                                "type": "string",
                                "enum": ["Strategic", "Critical", "DevilsAdvocate", "DomainExpert"]
                            },
                            "advisor_trigger": {
                                "type": "string",
                                "enum": ["BeforeFirstExecution", "BeforeEveryExecution", "OnConvergenceFailure"]
                            },
                            "provider_type": { "type": "string" },
                            "finalizer_type": { "type": "string" },
                            "tool_names": {
                                "type": "array",
                                "items": { "type": "string" },
                                "description": "Optional tool names from the tool catalogue to bind to this actor."
                            }
                        }
                    }
                },
                "evaluation_strategy": {
                    "type": "string",
                    "enum": ["Parallel", "Sequential", "FailFast", "Priority"]
                },
                "max_iterations": {
                    "type": "integer"
                },
                "abort_on_critical": {
                    "type": "boolean"
                }
            }
        }
        """;

    /// <summary>The <see cref="LlmTool"/> schema to pass to the composition meta-LLM.</summary>
    public static readonly LlmTool Schema = new()
    {
        Name        = ToolName,
        Description = "Submit a structured crew composition specification describing the executor, reviewers, advisors, grounding providers, and finalizers for the requested task.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>(SchemaJson)
    };
}
