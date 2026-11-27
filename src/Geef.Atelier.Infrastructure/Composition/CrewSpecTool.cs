using System.Text.Json;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Defines the structured tool schema the composition meta-LLM must use to submit its crew specification.
/// </summary>
internal static class CrewSpecTool
{
    /// <summary>The tool name used in LLM tool-choice requests.</summary>
    public const string ToolName = "submit_crew_spec";

    private const string SchemaJson = """
        {
            "type": "object",
            "required": ["mode", "domain", "model_domain", "rationale"],
            "properties": {
                "mode": {
                    "type": "string",
                    "enum": ["existing-template", "composed", "new"]
                },
                "domain": {
                    "type": "string",
                    "description": "Short lowercase domain hint for naming, deduplication and the domain-scoped packs, e.g. general, correspondence, legal, academic, marketing, speech, technical."
                },
                "model_domain": {
                    "type": "string",
                    "enum": ["General", "Correspondence", "Legal", "Academic", "Marketing", "Speech", "Technical"],
                    "description": "Model-matrix row for the crew. Legal and Academic allow paid domain reviewers: choose them only for real legal work (claims, contracts, litigation, statutes) or scholarly work (papers, grant proposals). Political speeches are Speech, spelling or editing is General, outreach to contractors is Correspondence."
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
                        "provider": { "type": "string", "description": "Ignored for LLM roles: the model matrix assigns provider and model from model_slot." },
                        "model": { "type": "string", "description": "Ignored for LLM roles: the model matrix assigns provider and model from model_slot." },
                        "model_slot": {
                            "type": "string",
                            "enum": ["Executor", "FactReviewer", "StyleReviewer", "DomainReviewer", "StrategicAdvisor", "CriticalAdvisor", "DomainExpertAdvisor", "TransformRewrite", "TransformCondense", "Helper"],
                            "description": "Role in the model matrix; the system assigns the model from it."
                        },
                        "settings": {
                            "type": "object",
                            "additionalProperties": { "type": "string" },
                            "description": "Optional settings for grounding providers and finalizers, e.g. {\"refinementMode\": \"0\"} to filter grounding results with the matrix refiner."
                        },
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
                        },
                        "pack_names": {
                            "type": "array",
                            "items": { "type": "string" },
                            "description": "Optional ordered specialization-pack names to bind to this actor (reused packs or names defined in top-level 'packs')."
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
                            "provider": { "type": "string", "description": "Ignored for LLM roles: the model matrix assigns provider and model from model_slot." },
                            "model": { "type": "string", "description": "Ignored for LLM roles: the model matrix assigns provider and model from model_slot." },
                            "model_slot": {
                                "type": "string",
                                "enum": ["Executor", "FactReviewer", "StyleReviewer", "DomainReviewer", "StrategicAdvisor", "CriticalAdvisor", "DomainExpertAdvisor", "TransformRewrite", "TransformCondense", "Helper"],
                                "description": "Role in the model matrix; the system assigns the model from it."
                            },
                            "settings": {
                                "type": "object",
                                "additionalProperties": { "type": "string" },
                                "description": "Optional settings for grounding providers and finalizers, e.g. {\"refinementMode\": \"0\"} to filter grounding results with the matrix refiner."
                            },
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
                            "provider": { "type": "string", "description": "Ignored for LLM roles: the model matrix assigns provider and model from model_slot." },
                            "model": { "type": "string", "description": "Ignored for LLM roles: the model matrix assigns provider and model from model_slot." },
                            "model_slot": {
                                "type": "string",
                                "enum": ["Executor", "FactReviewer", "StyleReviewer", "DomainReviewer", "StrategicAdvisor", "CriticalAdvisor", "DomainExpertAdvisor", "TransformRewrite", "TransformCondense", "Helper"],
                                "description": "Role in the model matrix; the system assigns the model from it."
                            },
                            "settings": {
                                "type": "object",
                                "additionalProperties": { "type": "string" },
                                "description": "Optional settings for grounding providers and finalizers, e.g. {\"refinementMode\": \"0\"} to filter grounding results with the matrix refiner."
                            },
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
                            "provider": { "type": "string", "description": "Ignored for LLM roles: the model matrix assigns provider and model from model_slot." },
                            "model": { "type": "string", "description": "Ignored for LLM roles: the model matrix assigns provider and model from model_slot." },
                            "model_slot": {
                                "type": "string",
                                "enum": ["Executor", "FactReviewer", "StyleReviewer", "DomainReviewer", "StrategicAdvisor", "CriticalAdvisor", "DomainExpertAdvisor", "TransformRewrite", "TransformCondense", "Helper"],
                                "description": "Role in the model matrix; the system assigns the model from it."
                            },
                            "settings": {
                                "type": "object",
                                "additionalProperties": { "type": "string" },
                                "description": "Optional settings for grounding providers and finalizers, e.g. {\"refinementMode\": \"0\"} to filter grounding results with the matrix refiner."
                            },
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
                            "provider": { "type": "string", "description": "Ignored for LLM roles: the model matrix assigns provider and model from model_slot." },
                            "model": { "type": "string", "description": "Ignored for LLM roles: the model matrix assigns provider and model from model_slot." },
                            "model_slot": {
                                "type": "string",
                                "enum": ["Executor", "FactReviewer", "StyleReviewer", "DomainReviewer", "StrategicAdvisor", "CriticalAdvisor", "DomainExpertAdvisor", "TransformRewrite", "TransformCondense", "Helper"],
                                "description": "Role in the model matrix; the system assigns the model from it."
                            },
                            "settings": {
                                "type": "object",
                                "additionalProperties": { "type": "string" },
                                "description": "Optional settings for grounding providers and finalizers, e.g. {\"refinementMode\": \"0\"} to filter grounding results with the matrix refiner."
                            },
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
                },
                "allow_mutating_tools": {
                    "type": "boolean",
                    "description": "When true, actors in this spec may bind Mutating-access tools. Must be explicitly set by the operator. Defaults to false."
                },
                "packs": {
                    "type": "array",
                    "description": "New specialization packs to create for this crew. Default scope is TaskBound (bound to this crew). Reference them from an actor's pack_names. Prefer reusing existing packs from the pack catalogue over creating new ones.",
                    "items": {
                        "type": "object",
                        "required": ["name", "specialization_text"],
                        "properties": {
                            "name": { "type": "string" },
                            "display_name": { "type": "string" },
                            "specialization_text": { "type": "string", "description": "The task/domain-specific prompt fragment." },
                            "scope": { "type": "string", "enum": ["General", "DomainScoped", "TaskBound"] },
                            "domain": { "type": "string", "description": "Required when scope is DomainScoped." },
                            "applicable_actor_types": {
                                "type": "array",
                                "items": { "type": "string", "enum": ["Executor", "Reviewer", "Advisor", "Any"] }
                            }
                        }
                    }
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
