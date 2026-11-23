namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>
/// Enforces the CLI-route rule (D-066): Claude models run through <see cref="ModelMatrix.ClaudeCli"/>, OpenAI
/// models through <see cref="ModelMatrix.CodexCli"/>. Also identifies vendor families and compares model ids
/// across vendor-prefixed and dotted/dashed spellings.
/// </summary>
public static class CliRoute
{
    private static readonly string[] VendorPrefixes = ["anthropic/", "openai/", "google/", "models/"];
    private static readonly string[] ClaudeFamilies = ["opus", "sonnet", "haiku", "fable"];

    /// <summary>
    /// Vendor family of the model <paramref name="model"/> served by <paramref name="provider"/>. An unambiguous model id
    /// (<c>claude…</c>/<c>anthropic/…</c>, <c>gpt-…</c>/<c>openai/…</c>/o-series, <c>gemini…</c>/<c>google/…</c>) decides;
    /// the provider only decides for ids that name no vendor. A contradictory binding such as
    /// <c>claude-cli/gpt-6.2-sol</c> therefore counts as OpenAI and is rerouted to codex.
    /// </summary>
    public static VendorFamily FamilyOf(string provider, string model)
    {
        var p = (provider ?? string.Empty).Trim().ToLowerInvariant();
        var m = (model ?? string.Empty).Trim().ToLowerInvariant();

        if (m.StartsWith("anthropic/", StringComparison.Ordinal) || m.StartsWith("claude", StringComparison.Ordinal))
            return VendorFamily.Anthropic;
        if (m.StartsWith("openai/", StringComparison.Ordinal) || m.StartsWith("gpt-", StringComparison.Ordinal) || IsOSeries(m))
            return VendorFamily.OpenAi;
        if (m.StartsWith("google/", StringComparison.Ordinal) || m.StartsWith("models/gemini", StringComparison.Ordinal) || m.StartsWith("gemini", StringComparison.Ordinal))
            return VendorFamily.Google;
        return p switch
        {
            ModelMatrix.ClaudeCli => VendorFamily.Anthropic,
            ModelMatrix.CodexCli => VendorFamily.OpenAi,
            "gemini-cli" => VendorFamily.Google,
            _ => VendorFamily.Other,
        };
    }

    /// <summary>True for OpenAI open-weight models (<c>gpt-oss</c>), which no subscription CLI serves.</summary>
    public static bool IsOpenWeight(string model) =>
        (model ?? string.Empty).Contains("gpt-oss", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a Claude or OpenAI model is bound to a provider other than its subscription CLI.</summary>
    public static bool IsNonCliRoute(string provider, string model)
    {
        var normalized = Normalize(provider, model);
        return !string.Equals(normalized.Provider, provider?.Trim(), StringComparison.OrdinalIgnoreCase)
               || !string.Equals(normalized.Model, model?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Rewrites a Claude/OpenAI binding onto its subscription CLI. A retired codex id (<see cref="IsRetiredCodexModel"/>)
    /// moves to the always-latest alias of its line (Sol, Terra) or to <see cref="ModelMatrix.RetiredOpenAiReplacement"/>;
    /// any other OpenAI id keeps its (lower-cased, unprefixed) name. Open-weight <c>gpt-oss</c> models and everything
    /// else are returned unchanged. Migration Step46 applies the same rule in SQL (parity-tested).
    /// </summary>
    public static ModelCandidate Normalize(string provider, string model)
    {
        var p = (provider ?? string.Empty).Trim();
        var m = (model ?? string.Empty).Trim();
        if (p.Length == 0 || m.Length == 0)
            return new ModelCandidate(p, m);

        switch (FamilyOf(p, m))
        {
            case VendorFamily.Anthropic when !string.Equals(p, ModelMatrix.ClaudeCli, StringComparison.OrdinalIgnoreCase):
                return new ModelCandidate(ModelMatrix.ClaudeCli, ClaudeAlias(m));
            case VendorFamily.OpenAi when !IsOpenWeight(m):
                var bare = StripVendorPrefix(m).ToLowerInvariant();
                var onCodex = string.Equals(p, ModelMatrix.CodexCli, StringComparison.OrdinalIgnoreCase);
                if (onCodex && !IsRetiredCodexModel(bare))
                    return new ModelCandidate(p, m);
                return new ModelCandidate(ModelMatrix.CodexCli, IsRetiredCodexModel(bare) ? ReplacementFor(bare) : bare);
            default:
                return new ModelCandidate(p, m);
        }
    }

    /// <summary>Lower-cased id without vendor prefix and with dots replaced by dashes.</summary>
    public static string CanonicalId(string model) =>
        StripVendorPrefix((model ?? string.Empty).Trim()).ToLowerInvariant().Replace('.', '-');

    /// <summary>
    /// True when both ids denote the same model regardless of vendor prefix and dot/dash spelling. An always-latest
    /// alias (<c>claude-opus-latest</c>, <c>gpt-sol-latest</c>) also matches any id of the same line, so the
    /// comparison stays safe when alias resolution is unavailable.
    /// </summary>
    public static bool IsSameModel(string modelA, string modelB)
    {
        var a = CanonicalId(modelA);
        var b = CanonicalId(modelB);
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;
        if (!a.EndsWith("-latest", StringComparison.Ordinal) && !b.EndsWith("-latest", StringComparison.Ordinal))
            return false;

        var lineA = LineOf(a);
        return lineA is not null && string.Equals(lineA, LineOf(b), StringComparison.Ordinal);
    }

    /// <summary>Vendor-qualified model line of a canonical id, e.g. <c>claude:opus</c> or <c>gpt:sol</c>; null when unknown.</summary>
    private static string? LineOf(string canonicalId)
    {
        if (canonicalId.StartsWith("claude", StringComparison.Ordinal))
        {
            var family = ClaudeFamilies.FirstOrDefault(f => canonicalId.Contains(f, StringComparison.Ordinal));
            return family is null ? null : $"claude:{family}";
        }

        if (!canonicalId.StartsWith("gpt-", StringComparison.Ordinal) || IsOpenWeight(canonicalId))
            return null;
        var segments = canonicalId.Split('-');
        var last = segments[^1] == "latest" && segments.Length > 2 ? segments[^2] : segments[^1];
        return last.Length > 0 && char.IsLetter(last[0]) ? $"gpt:{last}" : null;
    }

    private static string ReplacementFor(string retiredBareModel) =>
        retiredBareModel.EndsWith("-sol", StringComparison.Ordinal) ? ModelMatrix.CodexSolAlias
        : retiredBareModel.EndsWith("-terra", StringComparison.Ordinal) ? ModelMatrix.CodexTerraAlias
        : ModelMatrix.RetiredOpenAiReplacement;

    private static string ClaudeAlias(string model)
    {
        var lower = model.ToLowerInvariant();
        var family = ClaudeFamilies.FirstOrDefault(f => lower.Contains(f, StringComparison.Ordinal)) ?? "opus";
        return $"claude-{family}-latest";
    }

    /// <summary>
    /// True for a codex id the subscription no longer serves: gpt-4 and gpt-5 generations, the Luna line, <c>-mini</c>
    /// variants and the o-series. Expects the lower-cased id without vendor prefix.
    /// </summary>
    public static bool IsRetiredCodexModel(string bareModel) =>
        bareModel.StartsWith("gpt-4", StringComparison.Ordinal)
        || bareModel.StartsWith("gpt-5", StringComparison.Ordinal)
        || bareModel.Contains("luna", StringComparison.Ordinal)
        || bareModel.Contains("-mini", StringComparison.Ordinal)
        || IsOSeries(bareModel);

    private static bool IsOSeries(string model)
    {
        var bare = StripVendorPrefix(model);
        return bare.Length > 1 && bare[0] == 'o' && char.IsDigit(bare[1]);
    }

    private static string StripVendorPrefix(string model)
    {
        foreach (var prefix in VendorPrefixes)
        {
            if (model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return model[prefix.Length..];
        }

        return model;
    }
}
