using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Finalizers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Unit-level tests for finalizer chain behaviour: ordering, partial-success, and text accumulation.
/// These drive the executors directly — no full orchestrator required.
/// </summary>
public sealed class FinalizerChainTests
{
    private static FinalizerExecutionContext MakeCtx(Guid runId, string currentText) => new(
        RunId: runId,
        TemplateName: "chain-test",
        FinalText: currentText,
        CurrentText: currentText,
        RunCompletedAt: DateTimeOffset.UtcNow);

    private static FinalizerProfile EnrichProfile(string name, string enricher) => new(
        Name: name,
        DisplayName: name,
        Description: "test",
        FinalizerType: FinalizerType.MetadataEnrich,
        Settings: new Dictionary<string, string> { [MetadataEnrichSettings.KeyEnricherType] = enricher },
        IsSystem: false);

    private static MetadataEnrichFinalizerExecutor Enrich() =>
        new(NullLogger<MetadataEnrichFinalizerExecutor>.Instance);

    // ── Chain ordering ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Chain_ThreeMetadataEnrichers_ExecuteInOrder_EachSeesPreviousOutput()
    {
        var runId = Guid.NewGuid();
        var executor = Enrich();
        var order = new List<string>();

        var finalizers = new[]
        {
            EnrichProfile("step1", MetadataEnrichSettings.WordCountFooter),
            EnrichProfile("step2", MetadataEnrichSettings.ReadingLevel),
            EnrichProfile("step3", MetadataEnrichSettings.FrontMatter),
        };

        string text = "# Title\n\nSome test content.";
        foreach (var profile in finalizers)
        {
            var result = await executor.ExecuteAsync(profile, MakeCtx(runId, text), default);
            order.Add(profile.Name);
            if (result.UpdatedText is not null)
                text = result.UpdatedText;
        }

        Assert.Equal(["step1", "step2", "step3"], order);
        // step3 (front-matter) ran last: text should start with ---
        Assert.StartsWith("---", text.TrimStart());
        // Earlier enrichments are embedded inside front-matter-wrapped content
        Assert.Contains("words", text);
        Assert.Contains("reading level", text.ToLowerInvariant());
    }

    [Fact]
    public async Task Chain_SecondEnricher_ReceivesOutputFromFirst()
    {
        var runId = Guid.NewGuid();
        var executor = Enrich();
        const string initial = "Hello world.";

        var r1 = await executor.ExecuteAsync(
            EnrichProfile("s1", MetadataEnrichSettings.WordCountFooter),
            MakeCtx(runId, initial), default);
        var afterFirst = r1.UpdatedText!;

        // Second enricher receives the output of the first, not the original
        var r2 = await executor.ExecuteAsync(
            EnrichProfile("s2", MetadataEnrichSettings.FrontMatter),
            MakeCtx(runId, afterFirst), default);
        var afterSecond = r2.UpdatedText!;

        Assert.Contains("word_count", afterSecond);          // from front-matter
        Assert.Contains("min read", afterSecond);             // still present from word-count-footer
    }

    // ── Partial-success ────────────────────────────────────────────────────────

    [Fact]
    public async Task Chain_FailingEnricher_ProducesStatusArtifact_ChainContinues()
    {
        var runId = Guid.NewGuid();
        var executor = Enrich();

        var badProfile = new FinalizerProfile(
            Name: "bad-step",
            DisplayName: "Bad Enrich",
            Description: "test",
            FinalizerType: FinalizerType.MetadataEnrich,
            Settings: new Dictionary<string, string>
                { [MetadataEnrichSettings.KeyEnricherType] = "unknown-enricher-type" },
            IsSystem: false);

        var goodBefore = EnrichProfile("before", MetadataEnrichSettings.WordCountFooter);
        var goodAfter  = EnrichProfile("after",  MetadataEnrichSettings.ReadingLevel);

        string text = "Test content.";
        var results = new List<FinalizerExecutionResult>();

        foreach (var profile in new[] { goodBefore, badProfile, goodAfter })
        {
            var result = await executor.ExecuteAsync(profile, MakeCtx(runId, text), default);
            results.Add(result);
            if (result.UpdatedText is not null)
                text = result.UpdatedText;
        }

        // All three steps ran (no exception propagated)
        Assert.Equal(3, results.Count);

        // Step 1 succeeded and updated text
        Assert.NotNull(results[0].UpdatedText);
        Assert.Null(results[0].Artifact);

        // Step 2 failed: UpdatedText is null, Artifact is a Status record
        Assert.Null(results[1].UpdatedText);
        Assert.NotNull(results[1].Artifact);
        Assert.Equal(ArtifactType.Status, results[1].Artifact!.ArtifactType);

        // Step 3 succeeded despite middle failure
        Assert.NotNull(results[2].UpdatedText);
        Assert.Contains("reading level", results[2].UpdatedText!.ToLowerInvariant());
    }

    // ── FileExport: null UpdatedText contract ─────────────────────────────────

    [Fact]
    public async Task FileExport_Executor_ReturnsNullUpdatedText_FileArtifact()
    {
        using var tmp = new TempDir();
        var opts = Options.Create(new FinalizerOptions { ExportPath = tmp.Path });
        var executor = new FileExportFinalizerExecutor(opts, NullLogger<FileExportFinalizerExecutor>.Instance);
        var profile = new FinalizerProfile("export-md", "Export MD", "desc",
            FinalizerType.FileExport,
            new Dictionary<string, string> { [FileExportSettings.KeyFormat] = "markdown" },
            true);

        var result = await executor.ExecuteAsync(profile, MakeCtx(Guid.NewGuid(), "# Content\n\nText."), default);

        // FileExport must not update the current text in the chain
        Assert.Null(result.UpdatedText);
        Assert.NotNull(result.Artifact);
        Assert.Equal(ArtifactType.File, result.Artifact!.ArtifactType);
    }

    // ── Transform: updates text ────────────────────────────────────────────────

    [Fact]
    public async Task Transform_Executor_UpdatesCurrentText()
    {
        var fakeLlm = new ConstantTextClient("Polished text output.");
        var executor = new TransformFinalizerExecutor(
            new Tests.Llm.TestLlmClientResolver(fakeLlm),
            new NullPricingCatalog(),
            NullLogger<TransformFinalizerExecutor>.Instance);

        var profile = new FinalizerProfile("anti-ai-voice", "Anti-AI-Voice", "desc",
            FinalizerType.Transform,
            new TransformSettings("Polish the text.", "codex-cli", "gpt-5.5", 4096).ToDict(),
            true);

        var result = await executor.ExecuteAsync(profile, MakeCtx(Guid.NewGuid(), "Original text."), default);

        Assert.Equal("Polished text output.", result.UpdatedText);
        Assert.Null(result.Artifact);
    }

    // ── Fakes ──────────────────────────────────────────────────────────────────

    private sealed class ConstantTextClient(string text) : Geef.Atelier.Infrastructure.Llm.ILlmClient
    {
        public Task<Geef.Atelier.Infrastructure.Llm.LlmResponse> CompleteAsync(
            Geef.Atelier.Infrastructure.Llm.LlmRequest request, CancellationToken ct) =>
            Task.FromResult(new Geef.Atelier.Infrastructure.Llm.LlmResponse
            {
                Text = text,
                FinishReason = "stop",
                TokenUsage = new Geef.Atelier.Infrastructure.Llm.LlmTokenUsage { InputTokens = 5, OutputTokens = 10 },
            });
    }

    private sealed class NullPricingCatalog : Geef.Atelier.Application.Pricing.IPricingCatalog
    {
        public decimal? CalculateCostEur(string modelName, int i, int o) => null;
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"geef-chain-{Guid.NewGuid():N}");

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
