using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// Serialized crew snapshots for tests that create runs directly. The orchestrator no longer falls back to a
/// default crew, so every run it should pick up needs a stored snapshot.
/// </summary>
internal static class TestCrewSnapshots
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>A snapshot of the test loop (<see cref="TestLoop"/>); an orchestrator with <see cref="TestLoopAssembler"/> runs it.</summary>
    public static CrewSnapshot Runnable { get; } = TestLoop.Snapshot();

    /// <summary><see cref="Runnable"/> as stored in <c>Runs.CrewSnapshot</c>.</summary>
    public static string RunnableJson { get; } = Serialize(Runnable);

    /// <summary>
    /// A snapshot of the removed review loop (template klassik with advisors, grounding sources and a finisher) as runs before stage 8d
    /// stored it; synthetic, in the shape of the stored data. This version reads it for display, but no assembler accepts it.
    /// </summary>
    public const string RetiredKlassikJson = """
        {"schemaVersion":3,"templateName":"klassik",
         "executor":{"name":"default-executor","displayName":"Writer","description":"d","systemPrompt":"Write.","provider":"claude-cli","model":"claude-opus-latest","maxTokens":64000,"isSystem":true,"toolNames":null,"modelSlot":0,"modelDomain":null},
         "reviewers":[{"name":"briefing-fidelity","displayName":"Briefing fidelity","description":"d","systemPrompt":"Review.","provider":"codex-cli","model":"gpt-6.2-sol","maxTokens":64000,"isSystem":true,"toolNames":["web-search"],"modelSlot":1,"modelDomain":null}],
         "evaluationStrategy":0,"convergenceOverride":{"maxIterations":6,"abortOnCritical":false,"detectRegression":true,"stagnationThreshold":3},
         "advisors":[{"name":"briefing-clarifier","displayName":"Clarifier","description":"d","systemPrompt":"Advise.","provider":"claude-cli","model":"claude-sonnet-latest","maxTokens":8000,"mode":0,"trigger":0,"isSystem":true}],
         "groundingProviders":[{"name":"tavily-refined","displayName":"Web search","description":"d","providerType":"tavily","providerSettings":{"maxResults":"5"},"isSystem":true}],
         "finalizers":[{"name":"export-markdown","displayName":"Export","description":"d","finalizerType":0,"settings":{},"isSystem":true}],
         "runFinalizersOnMaxAttempts":false,"toolBindings":{"briefing-fidelity":[{"name":"web-search"}]},"promptCompositions":null,"crewDomain":0}
        """;

    /// <summary>Serializes <paramref name="snapshot"/> the way runs are stored.</summary>
    public static string Serialize(CrewSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);
}
