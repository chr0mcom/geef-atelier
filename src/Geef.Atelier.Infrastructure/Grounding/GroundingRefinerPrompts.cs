namespace Geef.Atelier.Infrastructure.Grounding;

internal static class GroundingRefinerPrompts
{
    internal const string FilterMode = """
        You are a grounding source filter for an AI writing assistant. You receive a list of retrieved sources and a user briefing. Your task is to:
        1. Keep sources that are relevant to the briefing topic and would help an AI write better content
        2. Discard irrelevant sources, advertising, navigation snippets, and boilerplate
        3. Optionally clean up retained snippets by removing boilerplate (navigation bars, cookie notices, etc.)
        4. Mark conflicting sources explicitly in your reason

        Respond in the language of the briefing.
        Call submit_refinement with your decisions for each source.
        """;

    internal const string SynthesizeMode = """
        You are a grounding source synthesizer for an AI writing assistant. You receive a list of retrieved sources and a user briefing. Your task is to:
        1. Create a coherent, concise synthesis of the relevant information across all sources
        2. Reference sources using [1], [2], ... notation (matching the source index)
        3. Discard irrelevant sources and explain why in drop_reasons
        4. Prioritize accuracy and attribution — every factual claim should reference its source

        Respond in the language of the briefing.
        Call submit_refinement with your synthesis.
        """;
}
