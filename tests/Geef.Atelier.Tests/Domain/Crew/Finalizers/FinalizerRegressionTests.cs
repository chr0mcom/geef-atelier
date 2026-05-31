using System.Text.Json;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Tests.Domain.Crew.Finalizers;

/// <summary>
/// Regression guard: ensures standard system templates (Klassik, domain templates) carry
/// exactly the expected finalizers. As of D-054 all standard templates ship with
/// <c>learning-extractor</c> as the single default finalizer.
/// </summary>
public sealed class FinalizerRegressionTests
{
    [Theory]
    [InlineData("klassik")]
    [InlineData("juristisch")]
    [InlineData("akademisch")]
    [InlineData("marketing")]
    public void SystemTemplates_HaveLearningExtractorFinalizer(string templateName)
    {
        Assert.True(SystemCrew.CrewTemplates.ContainsKey(templateName),
            $"Template '{templateName}' not found in SystemCrew.CrewTemplates");
        var template = SystemCrew.CrewTemplates[templateName];

        // All standard templates ship with learning-extractor as the sole default finalizer.
        Assert.Single(template.FinalizerProfileNames);
        Assert.Contains("learning-extractor", template.FinalizerProfileNames);
        Assert.False(template.RunFinalizersOnMaxAttempts);
    }

    [Fact]
    public void CrewSnapshot_OldFormat_DeserializesWithNullFinalizers()
    {
        // A snapshot without Finalizers (old format) should deserialize successfully
        // with Finalizers = null (trailing-optional with default null)
        const string oldSnapshotJson = """
            {
              "SchemaVersion": 1,
              "TemplateName": "klassik",
              "Executor": { "Name": "default-executor", "DisplayName": "Default", "Description": "",
                            "SystemPrompt": "", "Provider": "claude-cli", "Model": "anthropic/claude-opus-4.8",
                            "MaxTokens": null, "IsSystem": true },
              "Reviewers": [],
              "EvaluationStrategy": 0,
              "ConvergenceOverride": null,
              "Advisors": []
            }
            """;

        var snapshot = Geef.Atelier.Core.Domain.Crew.CrewSnapshot.Deserialize(oldSnapshotJson);

        // Should not throw; Finalizers should be null (omitted = default)
        Assert.NotNull(snapshot);
        Assert.Null(snapshot!.Finalizers);
        Assert.False(snapshot.RunFinalizersOnMaxAttempts);
    }

    [Fact]
    public void CrewSnapshot_WithFinalizers_Roundtrips()
    {
        var finalizerProfile = SystemCrew.ExportMarkdownProfile;
        var snapshot = new Geef.Atelier.Core.Domain.Crew.CrewSnapshot(
            SchemaVersion: 1,
            TemplateName: "test",
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: [],
            Finalizers: [finalizerProfile],
            RunFinalizersOnMaxAttempts: true);

        // Deserialize uses camelCase; serialize with the same policy for roundtrip
        var serializeOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var json = JsonSerializer.Serialize(snapshot, serializeOpts);
        var restored = Geef.Atelier.Core.Domain.Crew.CrewSnapshot.Deserialize(json);

        Assert.NotNull(restored);
        Assert.NotNull(restored!.Finalizers);
        Assert.Single(restored.Finalizers!);
        Assert.Equal("export-markdown", restored.Finalizers![0].Name);
        Assert.True(restored.RunFinalizersOnMaxAttempts);
    }
}
