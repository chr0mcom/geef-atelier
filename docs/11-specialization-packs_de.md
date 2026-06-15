# 11 — Spezialisierungs-Packs: Generische Akteure + wiederverwendbare Spezialisierungs-Schichten

> **Status:** umgesetzt (D-061). English: [`11-specialization-packs.md`](11-specialization-packs.md).

## Problem

Studio und der Auto-Crew-Composer erzeugten Akteure (Profile), deren System-Prompts zu eng auf *eine*
Aufgabe zugeschnitten waren. In einer anderen Crew wiederverwendet, folgt ein solcher Akteur seinem
aufgabengebackenen Prompt statt dem neuen Briefing — besonders gefährlich bei **Reviewern**, wo dies
die Konvergenz still korrumpiert.

## Lösung

Ein Akteur trägt nur noch einen **generischen Rollen-Prompt** (eine *Rolle*, keine *Aufgabe*) mit
einem `{specialization}`-Slot. Jegliche Aufgaben-/Domänen-Spezialisierung lebt in einer eigenen,
wiederverwendbaren, gescopten **`SpecializationPack`**-Schicht, die *pro Crew* an die Akteure gebunden
wird (geordnete Liste; mehrere Packs pro Akteur). Der effektive Prompt wird zur Snapshot-Bauzeit
komponiert und im Run-Snapshot reproduzierbar eingefroren. Weil der Akteur-Prompt nie aufgabenspezifisch
ist, kann beim Wiederverwenden nichts lecken. Die Scope-Logik (General / DomainScoped / TaskBound) liegt
auf den **Packs** — der natürlichen Stelle dafür.

```
GENERISCHER AKTEUR (Profil)                       SpecializationPack (eigene, gescopte Entität)
  SystemPrompt = Rollen-Prompt + {specialization}   name, specializationText, scope, domain?,
        │                                            applicableActorTypes[], owningCrewId?, isSystem
        │  Crew-Bindung: ActorPackBindings
        │  "reviewer:<name>" → [packA, packB, …]     (geordnet, mehrere pro Akteur)
        ▼
  CrewSnapshotBuilder.ApplyPacksAsync komponiert zur Snapshot-Bauzeit:
    effektiv = PromptComposer.Compose(rolePrompt, orderedPacks)
        → in den SystemPrompt des eingebetteten Profils geschrieben (Factory/Akteure unverändert)
        → zusätzlich PromptCompositions[] (Rolle + komponiert + Provenienz) für die Audit-UI
        ▼
  CrewSnapshot v3 → reproduzierbar, sichtbar in der Run-Detail-Audit-UI
```

## Datenmodell

- **`SpecializationPack`** (`Core/Domain/Crew/Specialization/`): `Name, DisplayName, Description,
  SpecializationText, Scope, Domain?, ApplicableActorTypes[], OwningCrewId?, IsSystem, Archived,
  CreatedAt?, UpdatedAt?, LastUsedAt?`.
  - `PackScope { General, DomainScoped, TaskBound }`.
  - `PackActorType { Any, Executor, Reviewer, Advisor, Grounding, Finalizer }` + `AppliesTo`.
- **`PromptComposer.Compose(rolePrompt, orderedPacks)`** — rein: ersetzt `{specialization}` durch die
  geordneten, mit `\n\n` verbundenen Pack-Texte; fehlt der Slot, werden sie unter `## Specialization`
  angehängt (definierter Fallback). Präzedenz: **last-in-sequence wins**.
- **`CrewTemplate.ActorPackBindings`** / **`CrewSpec.ActorPackBindings`** —
  `IReadOnlyDictionary<string, IReadOnlyList<string>>`, Key `"<actorType>:<profileName>"`
  (`ActorTypeKeys.BindingKey`), Wert = geordnete Pack-Namen. jsonb-Spalte auf `CrewTemplates`.
- **`CrewSnapshot` v3** — neues `PromptCompositions: IReadOnlyList<ActorPromptComposition>?`
  (`ActorType, ActorName, RolePrompt, ComposedPrompt, Packs[PackProvenance]`). Trailing-optional;
  v1/v2-Snapshots deserialisieren als `null`.

Komposition gilt nur für Akteure mit eigenem System-Prompt: **Executor, Reviewer, Advisor**.
Finalizer und Grounding-Provider haben kein Prompt-Feld; ihre Pack-Akteur-Typen existieren für die
Vorwärtskompatibilität, komponieren aber als No-op.

## Persistenz & Seeding

- System-Packs sind **Code-Konstanten** (`SystemPacks`), vom `SpecializationPackRepository` den Custom-
  DB-Rows vorangestellt — analog zum Akteur-Profil-Muster. Custom-Packs liegen in `specialization_packs`
  (Migration Step42).
- **System-Akteur-Umbau:** die sechs domänen-spezialisierten System-Reviewer (legal/academic/marketing)
  wurden durch **zwei generische Reviewer-Rollen** ersetzt — `domain-terminology-reviewer` und
  `substantive-rigor-reviewer` (je mit gemeinsamer Severity-Taxonomie + Slot). Die Domänen-Deltas
  wanderten in sechs DomainScoped-System-Packs (`legal-terminology`, `legal-clause-risk`,
  `academic-citation`, `academic-argumentation`, `marketing-voice`, `marketing-conversion`) plus zwei
  General-Beispiel-Packs (`concise-output`, `executive-tone`). Die Templates
  `juristisch`/`akademisch`/`marketing` binden diese Packs, sodass der komponierte Prompt
  verhaltensgleich bleibt.

## Komposition zur Laufzeit

`CrewService.ResolveSnapshotAsync` baut den Basis-Snapshot und ruft dann
`CrewSnapshotBuilder.ApplyPacksAsync` mit den `ActorPackBindings` des Templates/Specs und einem
Pack-Lookup. Je Akteur werden die geordneten gebundenen Packs aufgelöst (typ-inkompatible übersprungen),
der effektive Prompt **in den `SystemPrompt` des eingebetteten Profils** komponiert (`AtelierPipelineFactory`
und die `ProfileBased*`-Akteure bleiben unverändert) und die `PromptCompositions`-Provenienz protokolliert.
Genutzte Packs erhalten ein aktualisiertes `LastUsedAt` (steuert Auto-GC).

## Durchsetzung (Scope-Containment)

- **Picker / Service:** `ListForBindingAsync(actorType, crewDomain, owningCrewId)` liefert General +
  DomainScoped-passend-zur-Domäne + eigene TaskBound; **fremde TaskBound und archivierte Packs sind
  ausgeschlossen**; typ-gefiltert.
- **Crew-Kohärenz-Check (harter Block):** beim Speichern eines Custom-Templates wird jede Bindung
  geprüft — Pack existiert, ist typ-kompatibel und ist **kein** fremdes TaskBound-Pack
  (`OwningCrewId != template.Name`).
- **Composer:** der `PackCatalogGroundingProvider` (Typ `pack-catalog`) speist den wiederverwendbaren
  Pack-Katalog (fremde TaskBound ausgeschlossen) in den Composer; `CrewSpecValidator` Schritt 9 lehnt
  deterministisch unbekannte / typ-inkompatible / fremd-TaskBound-Packs und falsch gescopte neue Packs
  ab; die Composer-Reviewer `prompt-quality` und `reuse-correctness` wurden um Pack-Bewusstsein erweitert.

## Composer-Integration

- `CrewPartSpec.PackNames` (geordnet je Akteur) + `CrewSpecArtifact.NewPacks` (inline neue Packs,
  default TaskBound). Das `submit_crew_spec`-Schema erhielt `pack_names` je Akteur und ein top-level
  `packs`-Array. **Der Parser behält `tool_names`/`pack_names` auch auf `reuse`-Referenzen** — das Binden
  von Packs an wiederverwendete generische Reviewer ist der Hauptanwendungsfall.
- `CrewMaterializer` legt neue Packs an (TaskBound → an die erzeugte Crew gebunden), mappt
  inline→erstellte Namen und materialisiert `ActorPackBindings` aufs Template.

## Lebenszyklus

- **Cascade-Delete:** das Löschen eines Custom-Crew-Templates löscht seine TaskBound-Packs
  (`DeleteByOwningCrewAsync`) — keine Waisen.
- **Promote / Demote / Clone-to-Generalize:** Promote (TaskBound → DomainScoped/General) und
  Clone-to-Generalize sind durch ein LLM-**Generalitäts-Review** (`IPackGeneralityReviewer`, System-
  Default-Executor-Modell, fail-closed) gated, das Einmal-Spezifika ablehnt. Demote verengt General →
  DomainScoped. `FindReferencingTemplatesAsync` zeigt betroffene Crews.
- **Auto-GC:** `PackArchivalBackgroundService` archiviert Custom-General/DomainScoped-Packs, die länger
  als `PackGc.RetentionDays` (default 90) ungenutzt und von keinem Template referenziert sind. System-
  und TaskBound-Packs werden nie auto-archiviert. Archivierte Packs verschwinden aus Picker/Composer.

## Datenbank-Upgrade (saubere Neuaufsetzung, Konten + Auth bleiben)

Migration **Step42** legt `specialization_packs` und die `ActorPackBindings`-Spalte an und führt eine
saubere Neuaufsetzung durch (Betreiber-Policy): **nur Benutzerkonten und Auth/Credentials/Config
behalten, alles andere leeren**, damit die Plattform frisch mit den verbesserten generischen Akteuren +
Packs startet.

- **Behalten:** `Users`, alle OAuth-Tabellen, `mcp_server_configs`, `Providers` (LLM-Credentials),
  `SiteSettings`, `StudioSettings` sowie der DB-geseedete **System-Katalog** (System-Tools, -Finalizer,
  -Grounding-Provider — über `WHERE "IsSystem" = false`-DELETEs geschützt; sie haben keinen Reseed-Pfad).
- **Geleert:** Run-Historie, Custom-Profile/-Templates/-Packs, Crew-Embeddings, Studio-Analysen, die
  Knowledge-Base (alle Dokumente + Chunks), freigegebene Learnings und Custom-Tools.
- Die verbesserten Reviewer-/Executor-/Advisor-Prompts kommen aus **Code-Konstanten** und greifen
  automatisch — kein DB-Reseed nötig.

Vor dem Deploy ein vollständiges `pg_dump`-Backup ziehen (Sicherheitsnetz; Benutzer-/Auth-Daten bleiben
ohnehin in-place erhalten, daher kein separates Speichern/Wiederherstellen nötig).

## Wichtige Dateien

- **Core:** `Domain/Crew/Specialization/{SpecializationPack,PackScope,PackActorType,PromptComposition,
  PromptComposer,SystemPacks,ActorPromptComposition}.cs`; `CrewTemplate.cs`/`CrewSpec.cs`
  (`ActorPackBindings`); `CrewSnapshot.cs` (v3); `SystemPrompts.cs`/`SystemCrew.cs` (generische Akteure +
  rewired Templates); `Composition/CrewSpecArtifact.cs`; `Configuration/PackGcOptions.cs`;
  `Persistence/Crew/ISpecializationPackRepository.cs`.
- **Application:** `Crew/CrewSnapshotBuilder.cs` (`ApplyPacksAsync`), `Crew/CrewService.cs`
  (Komposition, Kohärenz-Check, Cascade, Promote/Demote/Clone), `Crew/IPackGeneralityReviewer.cs`.
- **Infrastructure:** `Persistence/Entities/SpecializationPackEntity.cs` +
  `Configurations/SpecializationPackConfiguration.cs` + `Persistence/Crew/SpecializationPackRepository.cs`;
  `Composition/{CrewSpecParser,CrewSpecTool,CrewSpecValidator,CrewMaterializer,PackGeneralityReviewer}.cs`;
  `Grounding/PackCatalogGroundingProvider.cs`; `Persistence/Migrations/20260615120000_Step42SpecializationPacks.cs`.
- **Web:** `Pages/{PacksIndex,PackEditor,PackView}.razor`; `UI/{SpecializationPackPicker,
  ActorPromptCompositionBlock}.razor`; `Services/{ISpecializationPackService,SpecializationPackService}.cs`;
  `Services/PackArchivalBackgroundService.cs`; `Components/Pages/CrewTemplateEditor.razor` +
  `RunDetail.razor`; `Layout/NavMenu.razor`; `Program.cs`.

## Verifikation (End-to-End)

1. **Sichere Wiederverwendung:** derselbe generische Akteur in zwei Crews mit verschiedenen Packs
   verhält sich je nach Pack.
2. **Mehrere Packs:** ein Akteur mit General + TaskBound erzeugt den korrekt komponierten effektiven
   Prompt (in der Audit-UI sichtbar, im Snapshot eingefroren).
3. **Scope-Ausschluss:** ein fremdes TaskBound-Pack ist im Picker/Composer-Katalog einer anderen Crew
   nicht wählbar/sichtbar.
4. **Composer:** ein Auto-Crew-Lauf erzeugt generische Akteure + passende Packs (vorhandene
   wiederverwendet, neue als TaskBound).
5. **System-Reseed:** `juristisch`/`akademisch`/`marketing` liefern via generische Reviewer + Packs
   verhaltensgleiche Ergebnisse.
6. **Lebenszyklus:** Cascade-Delete entfernt TaskBound-Packs; Promote nur nach bestandenem
   Generalitäts-Review; Auto-GC archiviert ein ungenutztes Pack.
