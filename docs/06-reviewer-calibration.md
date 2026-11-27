# Reviewer calibration

*[Deutsch](06-reviewer-calibration_de.md) · **English***

*Last updated: 2026-11-27 (D-091: stage 8d, the review loop this standard was written for was removed; sections marked as historical, convergence section updated; before that 2026-05-24, D-058: mandatory findings rule added; tool-call retry in cli-proxy)*

This document describes the **Atelier standard for reviewer severity** and the **convergence-policy strategy**.

> **Removed in stage 8d (D-091).** The old review loop, in which AI reviewers with their own prompts chose the severity of
> each finding, no longer exists. The severity taxonomy, the tool schema, the mandatory findings rule and the way to add
> reviewers below are **historical**. The reviewers of the direct path are described in
> [08-crew-system.md](08-crew-system.md#template-direct-two-reviewers-d-073); there the code decides the severity of a
> finding, and only `Error` or higher blocks.

## Severity taxonomy (Atelier standard)

*Historical (removed in stage 8d, D-091).* The review loop used four severity levels for reviewer findings. The definitions were binding for its reviewer prompts.

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

The tests that anchored this example were removed with the review loop.

## Tool schema

*Historical (removed in stage 8d, D-091).* The reviewers answered through the tool `submit_review` with
`"severity": { "enum": ["critical", "major", "minor", "info"] }`.

### Mandatory findings rule (D-058)

*Historical (removed in stage 8d, D-091).* Every reviewer of the review loop had to return at least one finding, enforced by
its system prompt and by one retry in code.

## Convergence policy

The policy is configured via `ConvergenceOptions` (`src/Geef.Atelier.Infrastructure/Configuration/`) and read from the section `Convergence` in `appsettings.json`. A setup in code may override the values:

| Value | `appsettings.json` | Override of `direct` |
|---|---|---|
| `MaxIterations` | 6 | 2 |
| `AbortOnCritical` | false | false |
| `DetectRegression` | true | false |
| `StagnationThreshold` | 3 | 2 |

So every run writes once and corrects at most once. Delivery of a run that does not converge is described in
[08-crew-system.md](08-crew-system.md#convergence-and-delivery).

### Rationale: AbortOnCritical=false as default

*Written for the review loop; the default still applies.* With `AbortOnCritical=true` (the old default from D-012) a single over-eager critical finding aborts the entire pipeline. That makes the system fragile against reviewer calibration errors. With `AbortOnCritical=false` the next iteration sees the findings and can address them.

D-025 documents the decision points behind this calibration.

## Adding new reviewers

*Removed in stage 8d (D-091).* There are no reviewer profiles in the database and no pages to create them. The checks of the
direct path are profiles in `SystemCrew` whose implementations `DirectPipelineAssembler` injects; a new check needs code.

## Learning-evaluation crew — strict calibration (D-054)

*Removed in stage 8d (D-091)* together with the learning loop. The earlier description is in the git history.
