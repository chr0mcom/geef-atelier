# 11 — Specialization Packs: Generic Actors + Reusable Specialization Layers

> **Status:** implemented (D-061). Deutsch: [`11-specialization-packs_de.md`](11-specialization-packs_de.md).

## Problem

Studio and the Auto-Crew composer used to produce actors (profiles) whose system prompts were baked
tightly to *one* task. Reused in another crew, such an actor follows its task-baked prompt instead of
the new briefing — most dangerously for **reviewers**, where it silently corrupts convergence.

## Solution

An actor now carries only a **generic role prompt** (a *role*, not a *task*) with a `{specialization}`
slot. All task/domain specialization lives in a separate, reusable, scoped **`SpecializationPack`**
layer that is bound *per crew* to its actors (an ordered list; several packs per actor). The effective
prompt is composed at snapshot-build time and frozen in the run snapshot for reproducibility. Because
the actor prompt is never task-specific, nothing can leak on reuse. The scope logic
(General / DomainScoped / TaskBound) lives on the **packs** — the natural place for it.

```
GENERIC ACTOR (profile)                         SpecializationPack (own scoped entity)
  SystemPrompt = role prompt + {specialization}   name, specializationText, scope, domain?,
        │                                          applicableActorTypes[], owningCrewId?, isSystem
        │  crew binding: ActorPackBindings
        │  "reviewer:<name>" → [packA, packB, …]   (ordered, several per actor)
        ▼
  CrewSnapshotBuilder.ApplyPacksAsync composes at snapshot-build time:
    effective = PromptComposer.Compose(rolePrompt, orderedPacks)
        → written into the embedded profile's SystemPrompt (factory/actors unchanged)
        → plus PromptCompositions[] (role + composed + provenance) for the audit UI
        ▼
  CrewSnapshot v3 → reproducible, visible in the run-detail audit UI
```

## Data model

- **`SpecializationPack`** (`Core/Domain/Crew/Specialization/`): `Name, DisplayName, Description,
  SpecializationText, Scope, Domain?, ApplicableActorTypes[], OwningCrewId?, IsSystem, Archived,
  CreatedAt?, UpdatedAt?, LastUsedAt?`.
  - `PackScope { General, DomainScoped, TaskBound }`.
  - `PackActorType { Any, Executor, Reviewer, Advisor, Grounding, Finalizer }` + `AppliesTo`.
- **`PromptComposer.Compose(rolePrompt, orderedPacks)`** — pure: replaces `{specialization}` with the
  ordered, `\n\n`-joined pack texts; if the slot is absent, appends them under `## Specialization`
  (defined fallback). Precedence: **last-in-sequence wins**.
- **`CrewTemplate.ActorPackBindings`** / **`CrewSpec.ActorPackBindings`** —
  `IReadOnlyDictionary<string, IReadOnlyList<string>>` keyed by `"<actorType>:<profileName>"`
  (`ActorTypeKeys.BindingKey`), value = ordered pack names. jsonb column on `CrewTemplates`.
- **`CrewSnapshot` v3** — new `PromptCompositions: IReadOnlyList<ActorPromptComposition>?`
  (`ActorType, ActorName, RolePrompt, ComposedPrompt, Packs[PackProvenance]`). Trailing-optional;
  v1/v2 snapshots deserialize as `null`.

Composition only applies to the actors that own a system prompt: **executor, reviewers, advisors**.
Finalizers and grounding providers have no prompt field; their pack actor-types exist for forward
compatibility but compose to a no-op.

## Persistence & seeding

- System packs are **code constants** (`SystemPacks`), concatenated ahead of custom DB rows by
  `SpecializationPackRepository` — mirroring the actor-profile pattern. Custom packs live in
  `specialization_packs` (Migration Step42).
- **System-actor rebuild:** the six domain-specialized system reviewers (legal/academic/marketing)
  were replaced by **two generic reviewer roles** — `domain-terminology-reviewer` and
  `substantive-rigor-reviewer` (each carrying the shared severity taxonomy + slot). The domain deltas
  moved into six DomainScoped system packs (`legal-terminology`, `legal-clause-risk`,
  `academic-citation`, `academic-argumentation`, `marketing-voice`, `marketing-conversion`) plus two
  General example packs (`concise-output`, `executive-tone`). The `juristisch`/`akademisch`/`marketing`
  templates bind these packs so the composed prompt stays behaviour-equivalent.

## Composition at runtime

`CrewService.ResolveSnapshotAsync` builds the base snapshot, then calls
`CrewSnapshotBuilder.ApplyPacksAsync` with the template/spec `ActorPackBindings` and a pack lookup.
For each actor it resolves the ordered bound packs (skipping type-incompatible ones), composes the
effective prompt **into the embedded profile's `SystemPrompt`** (so `AtelierPipelineFactory` and the
`ProfileBased*` actors need no changes), and records the `PromptCompositions` provenance. Used packs
get `LastUsedAt` touched (drives auto-GC).

## Enforcement (scope containment)

- **Picker / service:** `ListForBindingAsync(actorType, crewDomain, owningCrewId)` returns General +
  DomainScoped-matching-domain + own TaskBound; **foreign TaskBound and archived packs are excluded**;
  type-filtered.
- **Crew-coherence check (hard block):** saving a custom template validates every binding — pack
  exists, is type-compatible, and is **not** a foreign TaskBound pack (`OwningCrewId != template.Name`).
- **Composer:** the `PackCatalogGroundingProvider` (type `pack-catalog`) feeds the reusable pack
  catalogue (foreign TaskBound excluded) into the composer; `CrewSpecValidator` Step 9 deterministically
  rejects unknown / type-incompatible / foreign-TaskBound packs and mis-scoped new packs; the
  `prompt-quality` and `reuse-correctness` composer reviewers gained pack awareness.

## Composer integration

- `CrewPartSpec.PackNames` (ordered per actor) + `CrewSpecArtifact.NewPacks` (inline new packs,
  default TaskBound). The `submit_crew_spec` tool schema gained `pack_names` per actor and a top-level
  `packs` array. **The parser keeps `tool_names`/`pack_names` on `reuse` references** — binding packs
  to reused generic reviewers is the primary use case.
- `CrewMaterializer` creates new packs (TaskBound → owned by the new crew), maps inline→created names,
  and materializes `ActorPackBindings` onto the template.

## Lifecycle

- **Cascade-delete:** deleting a custom crew template deletes its TaskBound packs
  (`DeleteByOwningCrewAsync`) — no orphans.
- **Promote / Demote / Clone-to-Generalize:** Promote (TaskBound → DomainScoped/General) and
  Clone-to-Generalize are gated by an LLM **generality review** (`IPackGeneralityReviewer`, system
  default executor model, fail-closed) that rejects one-off specifics. Demote narrows General →
  DomainScoped. `FindReferencingTemplatesAsync` surfaces affected crews.
- **Auto-GC:** `PackArchivalBackgroundService` archives custom General/DomainScoped packs unused for
  longer than `PackGc.RetentionDays` (default 90) and unreferenced by any template. System and
  TaskBound packs are never auto-archived. Archived packs disappear from pickers/composer.

## Database upgrade (no system-data loss)

Migration **Step42** adds `specialization_packs` and the `ActorPackBindings` column, then performs the
concept's clean reseed: it clears **custom** crews/profiles/templates and run history. Crucially, the
profile/template deletes are scoped to `WHERE "IsSystem" = false`, so the **DB-seeded** system
finalizers (Step22/Step30) and system grounding providers (Step15) survive — deleting them would orphan
the system catalogue permanently. Take a `pg_dump` backup before deploy.

## Key files

- **Core:** `Domain/Crew/Specialization/{SpecializationPack,PackScope,PackActorType,PromptComposition,
  PromptComposer,SystemPacks,ActorPromptComposition}.cs`; `CrewTemplate.cs`/`CrewSpec.cs`
  (`ActorPackBindings`); `CrewSnapshot.cs` (v3); `SystemPrompts.cs`/`SystemCrew.cs` (generic actors +
  rewired templates); `Composition/CrewSpecArtifact.cs`; `Configuration/PackGcOptions.cs`;
  `Persistence/Crew/ISpecializationPackRepository.cs`.
- **Application:** `Crew/CrewSnapshotBuilder.cs` (`ApplyPacksAsync`), `Crew/CrewService.cs`
  (compose, coherence-check, cascade, promote/demote/clone), `Crew/IPackGeneralityReviewer.cs`.
- **Infrastructure:** `Persistence/Entities/SpecializationPackEntity.cs` +
  `Configurations/SpecializationPackConfiguration.cs` + `Persistence/Crew/SpecializationPackRepository.cs`;
  `Composition/{CrewSpecParser,CrewSpecTool,CrewSpecValidator,CrewMaterializer,PackGeneralityReviewer}.cs`;
  `Grounding/PackCatalogGroundingProvider.cs`; `Persistence/Migrations/20260615120000_Step42SpecializationPacks.cs`.
- **Web:** `Pages/{PacksIndex,PackEditor,PackView}.razor`; `UI/{SpecializationPackPicker,
  ActorPromptCompositionBlock}.razor`; `Services/{ISpecializationPackService,SpecializationPackService}.cs`;
  `Services/PackArchivalBackgroundService.cs`; `Components/Pages/CrewTemplateEditor.razor` +
  `RunDetail.razor`; `Layout/NavMenu.razor`; `Program.cs`.

## Verification (end-to-end)

1. **Safe reuse:** the same generic actor in two crews with different packs behaves per its pack.
2. **Multiple packs:** an actor with General + TaskBound packs yields the correctly composed effective
   prompt (visible in the audit UI, frozen in the snapshot).
3. **Scope exclusion:** a foreign TaskBound pack is not selectable/visible in another crew's picker or
   composer catalogue.
4. **Composer:** an auto-crew run produces generic actors + fitting packs (existing reused, new as
   TaskBound).
5. **System reseed:** `juristisch`/`akademisch`/`marketing` deliver behaviour-equivalent results via
   the generic reviewers + packs.
6. **Lifecycle:** cascade-delete removes TaskBound packs; promote only after a passing generality
   review; auto-GC archives an unused pack.
