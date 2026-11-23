using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Application.Crew.TemplateStudio;

/// <summary>Configuration options for the Template Studio meta-LLM calls.</summary>
public sealed class TemplateStudioOptions
{
    /// <summary>LLM provider name for Studio meta-analysis calls. Defaults to the model-matrix composer slot (D-066).</summary>
    public string Provider { get; set; } = ModelMatrix.Primary(ModelSlot.Composer).Provider;

    /// <summary>Model identifier for the meta-LLM analysis call. Defaults to the model-matrix composer slot (D-066).</summary>
    public string Model { get; set; } = ModelMatrix.Primary(ModelSlot.Composer).Model;

    /// <summary>Max tokens for the meta-LLM analysis response. Must accommodate several
    /// fully structured profile system prompts plus reasoning fields in a single tool call.</summary>
    public int MaxTokens { get; set; } = 8192;

    /// <summary>Cosine similarity threshold above which an existing profile is considered a duplicate.</summary>
    public double SimilarityThreshold { get; set; } = 0.85;

    /// <summary>Hard cap (seconds) on a single meta-LLM analysis call. Heavy reasoning models stream
    /// their response body well after the 200 headers arrive, so this must accommodate full generation
    /// of a large tool-call payload, not just connection time.</summary>
    public int AnalysisTimeoutSeconds { get; set; } = 600;

    /// <summary>Default field values applied when the meta-LLM omits a field for a proposed profile.</summary>
    public StudioDefaults Defaults { get; set; } = new();
}

/// <summary>Default values for proposed profile fields when the meta-LLM leaves them blank.</summary>
public sealed class StudioDefaults
{
    /// <summary>Hard lower bound for any proposed profile's max-tokens budget. Profiles produce
    /// fully structured output (multi-finding reviews, multi-point advice, full drafts); a small
    /// budget silently truncates that output, so even meta-LLM-proposed values are clamped up to this.</summary>
    public const int MinMaxTokens = 10000;

    // Reviewer defaults
    public string ReviewerModel { get; set; } = ModelMatrix.Primary(ModelSlot.FactReviewer).Model;
    public string ReviewerProvider { get; set; } = ModelMatrix.Primary(ModelSlot.FactReviewer).Provider;
    public int ReviewerMaxTokens { get; set; } = 16384;

    // Executor defaults
    public string ExecutorModel { get; set; } = ModelMatrix.Primary(ModelSlot.Executor).Model;
    public string ExecutorProvider { get; set; } = ModelMatrix.Primary(ModelSlot.Executor).Provider;
    public int ExecutorMaxTokens { get; set; } = 60000;

    // Advisor defaults
    public string AdvisorModel { get; set; } = ModelMatrix.Primary(ModelSlot.StrategicAdvisor).Model;
    public string AdvisorProvider { get; set; } = ModelMatrix.Primary(ModelSlot.StrategicAdvisor).Provider;
    public int AdvisorMaxTokens { get; set; } = 16384;
    public string AdvisorMode { get; set; } = "Strategic";
    public string AdvisorTrigger { get; set; } = "BeforeFirstExecution";

    // GroundingProvider defaults
    public string GroundingProviderType { get; set; } = "tavily";
    public string GroundingProviderProvider { get; set; } = ModelMatrix.Primary(ModelSlot.GroundingRefiner).Provider;

    // Template defaults
    public string EvaluationStrategy { get; set; } = "Sequential";
}
