namespace Geef.Atelier.Core.Domain.Crew.Specialization;

/// <summary>
/// Composes a generic actor role prompt with an ordered list of specialization packs into the
/// effective system prompt actually used at runtime. Pure and deterministic — no I/O.
/// </summary>
public static class PromptComposer
{
    /// <summary>
    /// Returns the effective prompt for <paramref name="rolePrompt"/> with <paramref name="orderedPacks"/>
    /// merged in. When no packs are supplied the role prompt is returned unchanged. Pack texts are
    /// concatenated in order (last-in-sequence wins on conflict); the block replaces the
    /// <see cref="PromptComposition.SpecializationSlot"/> if present, otherwise it is appended under
    /// <see cref="PromptComposition.FallbackHeading"/>.
    /// </summary>
    public static string Compose(string rolePrompt, IReadOnlyList<SpecializationPack> orderedPacks)
    {
        rolePrompt ??= string.Empty;

        var texts = orderedPacks
            .Select(p => p.SpecializationText?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .ToList();

        if (texts.Count == 0)
            return rolePrompt;

        var block = string.Join("\n\n", texts);

        if (rolePrompt.Contains(PromptComposition.SpecializationSlot, StringComparison.Ordinal))
            return rolePrompt.Replace(PromptComposition.SpecializationSlot, block, StringComparison.Ordinal);

        return $"{rolePrompt}\n\n{PromptComposition.FallbackHeading}\n{block}";
    }
}
