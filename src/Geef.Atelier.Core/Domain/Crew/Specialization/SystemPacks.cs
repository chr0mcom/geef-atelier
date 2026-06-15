namespace Geef.Atelier.Core.Domain.Crew.Specialization;

/// <summary>
/// Central catalogue of built-in system specialization packs. System packs are code constants
/// (mirroring <c>SystemCrew</c> profiles) and are concatenated ahead of custom DB packs by the
/// repository. The domain packs below carry the deltas that were previously baked into the now-removed
/// domain-specialized system reviewers; the generic reviewer roles supply the shared severity taxonomy.
/// </summary>
public static class SystemPacks
{
    // ── DomainScoped — for the generic domain-terminology-reviewer ───────────────────

    public static readonly SpecializationPack LegalTerminology = new(
        Name: "legal-terminology",
        DisplayName: "Legal Terminology (German)",
        Description: "German legal terminological precision: correct legal terms where the distinction matters.",
        SpecializationText: """
            Domain focus — German legal terminology. Judge whether the correct legal terms are used
            where the legal distinction matters.
            Key distinctions to verify: Anfechtung/Widerruf (§119 ff. vs. §355 BGB),
            Kündigung/Rücktritt (§346 BGB), Schulden/Haften/Bürgen, Mängel (§437 BGB)/Fehler,
            Vertragsstrafe (§339 BGB)/Schadensersatz, Vollmacht/Ermächtigung.
            Examples: using "Widerruf" where "Anfechtung" is correct changes legal meaning (critical);
            colloquial "zurücktreten" instead of "Rücktritt" where the distinction matters (major);
            "Verpflichtung" where "Verbindlichkeit" would be standard but the meaning holds (minor).
            """,
        Scope: PackScope.DomainScoped,
        Domain: "legal",
        ApplicableActorTypes: [PackActorType.Reviewer],
        OwningCrewId: null,
        IsSystem: true);

    public static readonly SpecializationPack AcademicCitation = new(
        Name: "academic-citation",
        DisplayName: "Academic Citation Adequacy",
        Description: "Scholarly citation adequacy: claims about the world must be attributed.",
        SpecializationText: """
            Domain focus — scholarly citation adequacy. Check whether claims about the world are
            properly attributed, distinguishing the author's own reasoning (no citation needed) from
            empirical or contested claims (citation needed).
            Common-knowledge heuristic: a claim that appears in any introductory textbook of the
            discipline and is uncontested needs no citation.
            Examples: an uncited statistic ("studies show 73%…") presented as fact is critical; a
            contested scholarly position asserted without attribution is major; a textbook definition
            stated without reference is minor.
            """,
        Scope: PackScope.DomainScoped,
        Domain: "academic",
        ApplicableActorTypes: [PackActorType.Reviewer],
        OwningCrewId: null,
        IsSystem: true);

    public static readonly SpecializationPack MarketingVoice = new(
        Name: "marketing-voice",
        DisplayName: "Marketing Audience Alignment",
        Description: "Target-audience alignment of marketing copy: register, reading level, resonance.",
        SpecializationText: """
            Domain focus — target-audience alignment of marketing copy. Evaluate reading level,
            cultural resonance, jargon-accessibility balance, and emotional register
            (aspirational/reassuring/urgent) against the stated audience.
            If no target audience is specified in the briefing, raise a "major" finding —
            audience-undefined copy cannot be validated.
            Examples: dense technical jargon in a mass-market campaign, or youth-brand slang for
            60+ readers, is critical; a register that alienates a significant portion of the audience
            is major; a single off-tone phrase in otherwise correct copy is minor.
            """,
        Scope: PackScope.DomainScoped,
        Domain: "marketing",
        ApplicableActorTypes: [PackActorType.Reviewer],
        OwningCrewId: null,
        IsSystem: true);

    // ── DomainScoped — for the generic substantive-rigor-reviewer ────────────────────

    public static readonly SpecializationPack LegalClauseRisk = new(
        Name: "legal-clause-risk",
        DisplayName: "Legal Clause Risk (German)",
        Description: "German contract-law clause risk and compliance.",
        SpecializationText: """
            Domain focus — German contract-law clause risk and compliance. Assess clauses for validity
            and unmanaged liability.
            Focus areas: §307 BGB fairness test for standard terms, consumer rights
            (§§312 ff./474 ff. BGB), AGB integration (§305 BGB), penalty-clause proportionality
            (§339 BGB), DSGVO Art. 13/14 references for consumer contracts.
            Examples: a liability exclusion void under §307 BGB, or a warranty disclaimer breaching
            §476 BGB, is critical; an ambiguous arbitration clause or a non-compete without reasonable
            limits is major; an undefined "reasonable time" or a damages cap without basis is minor.
            """,
        Scope: PackScope.DomainScoped,
        Domain: "legal",
        ApplicableActorTypes: [PackActorType.Reviewer],
        OwningCrewId: null,
        IsSystem: true);

    public static readonly SpecializationPack AcademicArgumentation = new(
        Name: "academic-argumentation",
        DisplayName: "Academic Argumentation Rigor",
        Description: "Logical and argumentative rigor of scholarly reasoning.",
        SpecializationText: """
            Domain focus — logical and argumentative rigor. Map Claim → Premise(s) → Warrant →
            Conclusion and verify each link explicitly.
            Fallacies to check: non sequitur, slippery slope, false dichotomy, hasty generalisation,
            appeal to authority without engagement, straw man, ad hominem.
            Examples: a conclusion that does not follow from its premises, or circular reasoning, is
            critical; a missing step leaving an unsupported leap (e.g. correlation treated as causation)
            is major; "therefore" where "suggests" would be more epistemically honest is minor.
            """,
        Scope: PackScope.DomainScoped,
        Domain: "academic",
        ApplicableActorTypes: [PackActorType.Reviewer],
        OwningCrewId: null,
        IsSystem: true);

    public static readonly SpecializationPack MarketingConversion = new(
        Name: "marketing-conversion",
        DisplayName: "Marketing Conversion Strength",
        Description: "Commercial conversion effectiveness of marketing copy.",
        SpecializationText: """
            Domain focus — commercial conversion effectiveness. Verify the copy drives a specific
            reader action.
            Checklist: CTA = action verb + specific outcome; USP visible within the first two sentences;
            a genuine urgency signal (scarcity, deadline, or outcome cost) without dark patterns; social
            proof where appropriate.
            Examples: a missing or vacuous CTA ("click here") is critical; an absent or buried value
            proposition is major; a CTA with a verb but no outcome ("Download" vs. "Download your free
            guide") is minor.
            """,
        Scope: PackScope.DomainScoped,
        Domain: "marketing",
        ApplicableActorTypes: [PackActorType.Reviewer],
        OwningCrewId: null,
        IsSystem: true);

    // ── General — reusable in any crew ───────────────────────────────────────────────

    public static readonly SpecializationPack ConciseOutput = new(
        Name: "concise-output",
        DisplayName: "Concise Output",
        Description: "Keep the output as concise as the task allows, without dropping required content.",
        SpecializationText: """
            Output discipline — be as concise as the task allows: prefer short, direct sentences, cut
            redundancy, and avoid filler. Never sacrifice required content or completeness for brevity.
            """,
        Scope: PackScope.General,
        Domain: null,
        ApplicableActorTypes: [PackActorType.Executor, PackActorType.Finalizer],
        OwningCrewId: null,
        IsSystem: true);

    public static readonly SpecializationPack ExecutiveTone = new(
        Name: "executive-tone",
        DisplayName: "Executive Tone",
        Description: "Confident, decision-oriented executive tone.",
        SpecializationText: """
            Tone — adopt a confident, executive register: lead with the conclusion, use active voice,
            and frame points in terms of decisions and outcomes rather than process.
            """,
        Scope: PackScope.General,
        Domain: null,
        ApplicableActorTypes: [PackActorType.Executor, PackActorType.Finalizer],
        OwningCrewId: null,
        IsSystem: true);

    /// <summary>All system packs in catalogue order.</summary>
    public static readonly IReadOnlyList<SpecializationPack> All =
    [
        LegalTerminology, AcademicCitation, MarketingVoice,
        LegalClauseRisk, AcademicArgumentation, MarketingConversion,
        ConciseOutput, ExecutiveTone
    ];

    /// <summary>System packs indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, SpecializationPack> ByName =
        All.ToDictionary(p => p.Name, StringComparer.Ordinal);
}
