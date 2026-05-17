# Reviewer calibration

*[Deutsch](06-reviewer-calibration_de.md) · **English***

*Last updated: 2026-05-17 (code references updated to the crew-profile system; severity taxonomy unchanged in substance)*

This document describes the **Atelier standard for reviewer severity** and the **convergence-policy strategy**. It is the normative reference for anyone adjusting reviewer prompts or adding new reviewers.

## Severity taxonomy (Atelier standard)

The Atelier pipeline uses four severity levels for reviewer findings. The definitions are binding — diverging interpretations in reviewer prompts are a bug.

| Severity | Meaning | Examples |
|---|---|---|
| **critical** | Substantial factual or logical error. A reader who trusts the text is actively misinformed. | Wrong name of a person; wrong year; wrong theorem; contradiction between two sections of the same text. |
| **major** | Important omission or clear inaccuracy that significantly reduces usefulness but does not directly misinform. | Central counter-argument missing; important caveat not mentioned; central source missing. |
| **minor** | Style improvement, request for precision, or clarity increase. The text is substantially correct. | Two sentences would be clearer combined; a term should be defined more precisely; phrasing is clumsy. |
| **info** | Optional note with no need to act. The reviewer observes something without demanding a change. | Pointer to further sources; observation about tone without criticism. |

### Anti-pattern: "technically correct" ≠ critical

The most common misclassification: a reviewer finds that something is technically correct but "could have been phrased more precisely" — and rates it **critical**.

**Rule:** if the reviewer's reasoning contains phrasings such as:
- "is correct, but..."
- "technically true"
- "accidentally right"
- "is fine in principle, however..."
- "the number is correct, although..."

...then the finding is **by definition not critical**. At most **minor**.

**Critical means: the text is wrong.** Not: "could be more precise."

### Negative example (Hadwiger–Nelson)

The Hadwiger–Nelson problem triggered this misclassification:

> *"The description of the Moser spindle is factually wrong: the Moser spindle consists of 7 vertices and 11 edges, not 'seven points' in general — this is accidentally correct, but the statement is imprecise."*

**Analysis:** the reviewer themselves writes "accidentally correct". The number 7 is right. The criticism is a request for precision (graph-theoretical terminology "vertices/edges" vs. "points"). That is **minor**, not critical.

The Hadwiger–Nelson taxonomy is anchored as `[InlineData]` in `SeverityClassificationTests`.

## Tool schema

The `submit_review` tool accepts:
```json
"severity": { "enum": ["critical", "major", "minor", "info"] }
```

**Backwards compatibility:** `ProfileBasedReviewer.MapSeverity()` (in `src/Geef.Atelier.Infrastructure/Pipeline/ProfileBasedReviewer.cs`) still accepts `"error"` (→ `SdkSeverity.Error`) and `"warning"` (→ `SdkSeverity.Warning`) as a fallback in case the LLM deviates from the schema.

## Convergence policy

The policy is configured via `ConvergenceOptions` (`src/Geef.Atelier.Infrastructure/Configuration/`) and read from `appsettings.json`:

```json
{
  "Convergence": {
    "MaxIterations": 3,
    "AbortOnCritical": false,
    "DetectRegression": true,
    "StagnationThreshold": 3
  }
}
```

### Rationale: AbortOnCritical=false as default

With `AbortOnCritical=true` (the old default from D-012) a single over-eager critical finding aborts the entire pipeline. That makes the system fragile against reviewer calibration errors.

With `AbortOnCritical=false`:
- The pipeline iterates up to `MaxIterations=3` times.
- Each iteration sees the previous one's findings and can address them.
- Only on stagnation (identical findings across `StagnationThreshold=3` iterations) does the pipeline abort — which then is a legitimate abort.

### When AbortOnCritical=true makes sense

When a deployment requires absolute quality assurance and reviewer calibration is considered reliable — e.g. domain-specialized reviewers with vetted prompts (roadmap step 8: domain specialization).

## Adding new reviewers

Since the crew system (D-028) reviewers are **data-driven profiles**, no longer code classes (`LlmReviewer`/`AtelierSystemPrompts` were removed). A new system reviewer:

1. Add the system prompt as a `public const string` in `src/Geef.Atelier.Core/Domain/Crew/SystemPrompts.cs`.
2. Reuse the **complete severity-taxonomy block** from an existing system reviewer (e.g. `briefing-fidelity` or `clarity`) — do not invent a separate schema.
3. Copy the anti-pattern section and the Hadwiger–Nelson example along with it.
4. Register the reviewer as a `ReviewerProfile` constant in `SystemCrew` (`src/Geef.Atelier.Core/Domain/Crew/SystemCrew.cs`) — with provider/model per the model-pluralism convention (foreign model relative to the executor).
5. If needed, add it to the reviewer list of a system `CrewTemplate` in `SystemCrew`. Custom reviewers are instead created via `ICrewService` / the `/crew/profiles/reviewers` UI — no code needed.
6. Extend `SeverityClassificationTests` with the new reviewer name (if tested reviewer-specifically).

D-025 documents the decision points behind this calibration.
