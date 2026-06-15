using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Providers;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Deterministic-prompt LLM reviewer that judges whether a pack's specialization text is general enough
/// to be promoted/cloned for reuse. Uses the system default executor's provider/model. Fails open on
/// provider errors (returns Approved=false with a diagnostic concern, never throws to the caller).
/// </summary>
internal sealed class PackGeneralityReviewer(
    ILlmClientResolver resolver,
    ILogger<PackGeneralityReviewer> logger) : IPackGeneralityReviewer
{
    private const string SystemPrompt = """
        You review whether a reusable "specialization pack" text is GENERAL enough to be reused across
        multiple crews/tasks, or whether it contains ONE-OFF specifics (a particular document, client,
        date, project name, or single task) that would leak if reused elsewhere.
        General domain/role guidance (e.g. legal terminology rules, citation conventions) is fine.
        One-off specifics (e.g. "the Q3 2026 ACME merger memo", "this landing page for product X") are NOT.
        Respond with ONLY a JSON object:
        {"generalizable": true|false, "concerns": ["short reason", ...]}
        When generalizable is true, concerns must be an empty array.
        """;

    public async Task<GeneralityReviewResult> ReviewAsync(
        SpecializationPack pack, PackScope targetScope, CancellationToken ct = default)
    {
        try
        {
            var (client, model, maxTokens) = resolver.ForProfile(
                SystemCrew.DefaultExecutorProfile.Provider,
                SystemCrew.DefaultExecutorProfile.Model,
                4096);

            var userPrompt = $"""
                Target scope after generalization: {targetScope}.
                Pack name: {pack.Name}
                Specialization text:
                ---
                {pack.SpecializationText}
                ---
                Is this text general enough for the target scope? Reply with the JSON object only.
                """;

            var response = await client.CompleteAsync(new LlmRequest
            {
                Model = model,
                SystemPrompt = SystemPrompt,
                UserPrompt = userPrompt,
                MaxTokens = maxTokens,
            }, ct);

            return ParseVerdict(response.Text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pack generality review failed for '{Pack}'; treating as not approved.", pack.Name);
            return new GeneralityReviewResult(false, [$"Generality review could not run: {ex.Message}"]);
        }
    }

    private static GeneralityReviewResult ParseVerdict(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new GeneralityReviewResult(false, ["Empty review response."]);

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return new GeneralityReviewResult(false, ["Review response was not valid JSON."]);

        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("generalizable", out var gEl) && gEl.ValueKind == JsonValueKind.True;
            var concerns = new List<string>();
            if (root.TryGetProperty("concerns", out var cEl) && cEl.ValueKind == JsonValueKind.Array)
                concerns.AddRange(cEl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
            return new GeneralityReviewResult(ok, ok ? [] : (concerns.Count > 0 ? concerns : ["Not generalizable."]));
        }
        catch
        {
            return new GeneralityReviewResult(false, ["Review response could not be parsed."]);
        }
    }
}
