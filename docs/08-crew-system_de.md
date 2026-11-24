# 08 — Crew-System (PS-5)

*[English](08-crew-system.md) · **Deutsch***

Letzte Aktualisierung: 2026-11-24 (D-067: strenges Lesen des Snapshots, mindestens ein Reviewer, Outcome nach den Finalizern, Warnungen am Lauf, System-Grounding wieder über den Provider-Typ-Weg, Learning standardmäßig pausiert; zweite Prüfrunde: Reviewer-Prüfung beim Speichern, Vektor-Scope `both`, `grounding.scope_unknown`, `grounding.tool_failed` reserviert, Finalizer und Abbruch; zuvor 2026-11-23, D-066/12: `model_domain` explizit, ein Zuweisungsprädikat; D-066/10–11: Codex-Aliase, Host-Abgleich, verfügbare Modelle nur laut Live-Katalog; zuvor 2026-11-22, D-066: Modellmatrix — System-Profile und Abschnitt Modellmatrix neu; zuvor 2026-06-04, D-059: Auto-Crew)

## Überblick

Das Crew-System ersetzt die in PS-2 hartkodierte Dreier-Crew (Executor + BriefingTreueReviewer + KlarheitReviewer) durch ein konfigurierbares Profil- und Template-System. Jeder Run erhält beim Einreichen einen vollständig eingebetteten **CrewSnapshot**, der die Reproduzierbarkeit des Runs auch dann garantiert, wenn Profile später geändert oder gelöscht werden.

## Kernbegriffe

| Begriff | Bedeutung |
|---|---|
| **ExecutorProfile** | LLM-Akteur der den Draft erstellt. Trägt System-Prompt, Provider, Modell, MaxTokens. |
| **ReviewerProfile** | LLM-Akteur der den Draft bewertet. Gleiche Felder. `Priority` für sequenzielle Strategien via `IReviewer.Priority`. |
| **CrewTemplate** | Komponiert Executor + Reviewers + EvaluationStrategy + optionalen ConvergenceOverride + Advisor-Profile. |
| **CrewSnapshot** | Vollständig eingebettete Kopie des CrewTemplates (inkl. aller Profil-Daten) zum Zeitpunkt der Run-Einreichung. Persistiert als JSONB auf `Runs.CrewSnapshot`. |
| **AdvisorProfile** | LLM-Akteur für konsultative Pässe vor oder nach der Execution. Trägt `AdvisorMode` + `AdvisorTrigger`. Funktional ab PS-7. |
| **FinalizerProfile** | Nachverarbeitungs-Akteur, der nach der GEEF-Convergence-Schleife läuft. Trägt `FinalizerType` + typisierte Settings. Erzeugt `RunArtifact`-Datensätze. Funktional ab Step22 (D-044). |

## EvaluationStrategies

| Enum-Wert | SDK-Klasse | Verhalten |
|---|---|---|
| `Parallel` | `ParallelEvaluationStrategy` | Alle Reviewer parallel, alle Findings gesammelt. Standard. |
| `Sequential` | `SequentialEvaluationStrategy` | Reviewer nacheinander in Listen-Reihenfolge, alle abwarten. |
| `FailFast` | `FailFastEvaluationStrategy` | Wie Sequential, Abbruch nach erstem Critical-Finding. |
| `Priority` | `PriorityOrderedEvaluationStrategy` | Reviewer in `Priority`-Reihenfolge (nicht Listenreihenfolge). |

**Hinweis:** Bei `Parallel` ist die Reihenfolge in `ReviewerProfileNames` nur dokumentatorisch. Bei `Sequential` und `Priority` ist sie signifikant.

## System-Profile (Code-Konstanten)

Definiert in `Geef.Atelier.Core.Domain.Crew.SystemCrew` (read-only, versioniert mit dem Code). Seit D-066 enthält `SystemCrew` keine Modell-Literale mehr: Jede LLM-Rolle trägt einen `ModelSlot`, und ihr angezeigtes Binding ist das Matrix-Primärmodell. Das tatsächliche Modell setzt die Modellmatrix pro Run (siehe Abschnitt **Modellmatrix (D-066)**).

| Name | Typ | Rolle (`ModelSlot`) | Primärmodell (General) |
|---|---|---|---|
| `default-executor` | Executor | Executor | `claude-cli` / `claude-opus-latest` (Technical: Fable) |
| `briefing-fidelity` | Reviewer | FactReviewer | `codex-cli` / `gpt-astra-latest` |
| `clarity` | Reviewer | StyleReviewer | `codex-cli` / `gpt-sol-latest` |
| `domain-terminology-reviewer` | Reviewer | StyleReviewer | `codex-cli` / `gpt-sol-latest` |
| `substantive-rigor-reviewer` | Reviewer | DomainReviewer | `codex-cli` / `gpt-astra-latest` (Legal/Academic: Gemini 4 Neon) |
| `crew-composer-executor` | Executor | Composer | `claude-cli` / `claude-opus-latest` |
| `crew-composer-completeness`, `crew-composer-tool-binding` | Reviewer | FactReviewer | Ring: Astra, dann Sonnet |
| `crew-composer-fit`, `crew-composer-reuse-correctness` | Reviewer | DomainReviewer | Ring: Astra, dann Fable |
| `crew-composer-prompt-quality` | Reviewer | StyleReviewer | `codex-cli` / `gpt-sol-latest` |
| `crew-spec-validator` | Reviewer | — (deterministisch) | — |

**System-Templates** (vier + Composer), jeweils mit Crew-Gebiet (`CrewTemplate.Domain`):

- `klassik`: General, Evaluation `Parallel`, keine Advisors; reproduziert das ursprüngliche PS-2-Verhalten.
- `juristisch`: Legal, `Sequential`, Advisor `legal-domain-expert`.
- `akademisch`: Academic, `Sequential`, Advisor `academic-rigor-advisor`.
- `marketing`: Marketing, `Parallel`, keine Advisors.
- `crew-composer`: General.

Alle vier Standard-Templates liefern drei Standard-Grounding-Provider (`tavily-basic`, `run-attachments`, `learning-retriever-default`) und einen Standard-Finalizer (`learning-extractor`) aus. Einzige Ausnahme ist das `learning-evaluation`-Template — es enthält weder `learning-retriever-default` noch `learning-extractor` (Rekursionsstopp). Seit D-067 laufen die Learning-Teile leer, solange `Learning:Enabled` nicht gesetzt ist (siehe [Kontinuierlicher Lernzyklus](#kontinuierlicher-lernzyklus-d-054)).

## Modellmatrix (D-066)

**Grundsatz:** Ein Profil sagt, welche **Rolle** es spielt; die Crew sagt, in welchem **Gebiet** sie arbeitet. Das Modell setzt das System aus `Core/Domain/Crew/Models/ModelMatrix`. Claude-Modelle laufen immer über `claude-cli`, OpenAI-Modelle immer über `codex-cli`. Andere Wege werden beim Speichern umgeschrieben (`CliRoute`, `ManagedBindingExtensions.ForSave`). Die Recherche steht in `reports/2026-11-22-modellrecherche.md`, die Begründung in D-066.

| Rolle | Kandidaten (Reihenfolge) |
|---|---|
| Executor | Opus → Sol; Technical: Fable → Opus → Astra |
| FactReviewer | Astra → Opus → Neon → Sonnet |
| StyleReviewer | Sol → Opus → Sonnet |
| DomainReviewer | Legal/Academic: Neon → Astra → Opus → Fable → Sonnet; übrige: Astra → Neon → Opus → Fable → Sonnet |
| StrategicAdvisor / CriticalAdvisor | Fable → Astra |
| DomainExpertAdvisor | Technical: Fable → Astra; Academic: Astra → Opus; übrige: Opus → Astra |
| GroundingRefiner | Sonnet → Sol |
| TransformRewrite / TransformCondense | Sol → Sonnet / Sonnet → Sol |
| Composer | Opus → Sonnet → Sol |
| Helper | Haiku → Sonnet |

Gebiete: General, Correspondence, Legal, Academic, Marketing, Speech, Technical.

**Verwaltet oder manuell:**

- `ModelSlot` gesetzt bedeutet verwaltet: Das gespeicherte Binding ist nur Anzeige. `ModelSlot = null` bedeutet manuell: Das Binding gilt so, wie es gespeichert ist (nur CLI-normalisiert).
- Grounding-Refiner und Transform-Finalizer nutzen stattdessen den Settings-Marker `modelSlot` bzw. `ModelSlot`.
- Im Editor heißt das „Model assignment: Automatic by role / Manual“.

**Zuweisung beim Run-Start** (`IModelAssignmentService.ApplyAsync`, zwischen Packs und Alias-Auflösung):

1. Gebiet = Profil-Override, sonst Crew-Gebiet, sonst General. Das Crew-Gebiet setzt der Composer ausdrücklich als `model_domain` (`CrewSpecArtifact.EffectiveModelDomain`). Es gibt keine Ableitung aus Stichwörtern. Fehlt das Feld oder ist es ungültig, warnt der Validator, und es gilt der freie `domain`-String, sofern er ein exakter Gebietsname ist (alte Artefakte), sonst General.
2. Kandidaten, die der Live-Katalog nicht wirklich meldet, entfallen; die mit der statischen Liste zusammengeführte Picker-Liste zählt dafür nicht. Ist der Katalog nicht erreichbar, gilt der Kandidat als verfügbar. Ein HTTP-Provider ohne gesetzten API-Schlüssel gilt als nicht verfügbar.
3. Reviewer und Advisor laufen nicht auf dem Modell des Executors (alias-aufgelöst), solange es eine erlaubte Alternative gibt; sonst nur mit Plurality-Warnung. Reviewer bevorzugen eine andere Familie. Ein manueller Reviewer auf dem Executor-Modell bleibt, wird aber gewarnt.
4. Reviewer derselben Rolle bekommen reihum ein Modell aus den ersten 3 geeigneten Kandidaten (Ring).
5. Bezahlte Kandidaten gibt es außerhalb von Legal/Academic erst ab 3 Reviewern der Rolle. Je Crew sind höchstens 2 bezahlte Reviewer erlaubt.
6. Bleibt nach den harten Regeln nichts übrig, gilt der erste unbezahlte Kandidat, mit Warnung. Ein übersprungener Primärkandidat wird ebenfalls geloggt.

Die Schritte 2–6 sind eine einzige Regel (`ModelChooser`): Harte Regeln (H1 nutzbar, H2 Bezahlregel) werden nie gelockert, weiche Regeln (Live-Katalog, Executor-Modell, Ring, Wiederholung im Slot, Familie, Matrixposition) sortieren nur. Der Ring wird je Wahl aus den gerade erlaubten Kandidaten gebildet; das Gedächtnis über bereits vergebene Modelle gilt je Rolle über alle Profil-Gebiete. `ModelAssignmentInvariantTests` prüft die Eigenschaften über alle Kombinationen.

**Immer aktuelle Modelle (D-066/10):**

- **Aliase:** Die Matrix nennt Familien-Aliase, keine Versionen: `claude-opus-latest` usw. und `gpt-sol-latest`/`gpt-astra-latest`. Der cli-proxy löst die Codex-Aliase auf das neueste gelistete Modell der Linie in `models_cache.json` auf, die Claude-Aliase über die claude-CLI. Kommt etwa `gpt-6.3-sol`, nutzen alle verwalteten Profile es ohne Codeänderung. Der Run-Snapshot hält die konkrete ID fest.
- **Nächtlicher Job:** `scripts/cli-proxy-selfupdate.sh` (Cron 03:40) hält die CLIs im Container und auf dem Host aktuell und stellt das Host-Standardmodell von codex auf die neueste Sol-Version. Hintergrund: Neue Modelle gibt der Server nur an aktuelle CLI-Versionen frei.
- **Code-Änderung nötig** ist nur noch bei einer neuen Modelllinie, einem neuen Anbieter oder einer neuen Rolle: die Zeile in `ModelMatrix` ändern. Der Konsistenztest `ModelMatrixTests` meldet Drift zu `CodexSubscriptionModels`, `StaticModelFallback.ForCodexCli`, `SystemProviders` und `_FALLBACK_MODELS` im cli-proxy.

**Composer-Settings:** Inline-Grounding und -Finalizer des Composers dürfen nur unkritische Einstellungen setzen (Whitelist `CrewPartSettingsPolicy`). Ziele, Zugangsdaten, Variablennamen und Modellbindungen werden verworfen und als Warnung gemeldet. Inline-`rest-api` und Inline-`ExternalSink` sind kritisch; dafür ein freigegebenes Profil wiederverwenden.

Verwaltete Profile folgen nach dem Deploy automatisch; manuelle Profile bleiben unverändert.

## Custom-Profile

- Werden in der DB (`ReviewerProfiles`, `ExecutorProfiles`, `CrewTemplates`) gespeichert.
- Name erhält automatisch den Prefix `"custom-"` (idempotent, kein Doppelpräfix).
- System-Profile sind read-only: Update/Delete wirft `InvalidOperationException("System profile is read-only — copy it as a custom variant.")`.
- API: `ICrewService.CreateCustomReviewerProfileAsync(profile)`.

## CrewSnapshot-Format (SchemaVersion 1)

> Das folgende Beispiel zeigt die **Struktur**. Die `provider`/`model`-Werte sind
> illustrativ — die aktuell gültigen System-Werte stehen in der Tabelle
> „System-Profile" oben; ein realer Snapshot enthält die zum Submit-Zeitpunkt
> gültigen Werte.

```json
{
  "schemaVersion": 1,
  "templateName": "klassik",
  "executor": {
    "name": "default-executor",
    "displayName": "Default Executor",
    "systemPrompt": "...",
    "provider": "openrouter",
    "model": "anthropic/claude-opus-4.8",
    "maxTokens": null,
    "isSystem": true
  },
  "reviewers": [
    { "name": "briefing-fidelity", "provider": "openrouter", "model": "google/gemini-3.5-flash", ... },
    { "name": "clarity",           "provider": "openrouter", "model": "openai/gpt-5.6-luna",    ... }
  ],
  "evaluationStrategy": "Parallel",
  "convergenceOverride": null,
  "advisors": []
}
```

Serialisiert mit `JsonNamingPolicy.CamelCase`. Gespeichert auf `Runs.CrewSnapshot` (JSONB).

**Snapshot für einen Lauf lesen (D-067).** Der Orchestrator liest den Snapshot streng über
`CrewSnapshot.DeserializeForRun`. Ein fehlender, unlesbarer oder leerer Snapshot, eine unbekannte
Schema-Version oder ein Snapshot ohne Executor-, Reviewer- oder Advisor-Liste löst
`InvalidCrewSnapshotException` aus, und der Lauf endet `Failed` mit verständlicher Meldung. Der frühere
stille Rückfall auf die `klassik`-Crew ist entfernt. Das tolerante `CrewSnapshot.Deserialize` bleibt nur für
die Anzeige. Ein Lauf mit Anhängen wird ohne Snapshot abgelegt, bis seine Anhänge bereit sind
([02-architecture](02-architecture_de.md#vorbereitungs-zeitlimit-für-läufe-mit-anhängen)).

**Mindestens ein Reviewer.** Eine Crew ohne Reviewer kann nicht laufen: `AtelierPipelineFactory` baut die
Pipeline nicht, und der Lauf endet `Failed` mit Meldung. Der frühere `AutoApproveReviewer`, der jeden
Entwurf still freigab, ist entfernt. Eine Crew-Vorlage ohne Reviewer lässt sich weder anlegen noch
speichern (`CrewTemplateRules.EnsureHasReviewer`, genutzt von `CrewService` und vom Vorlagen-Editor);
Template Studio und Composer verlangen ebenfalls einen Reviewer.

## Advisor-Pässe (PS-7)

Advisors sind konsultative LLM-Akteure, die zu definierten Zeitpunkten in der Pipeline ausgeführt werden. Ihr Output fließt als gekennzeichneter Kontext-Block in den Run — der Executor und nachfolgende Reviewer sehen ihn, ohne dass das Geef-SDK-Kern modifiziert werden muss.

### AdvisorProfile-Schema

```csharp
public sealed record AdvisorProfile(
    string Name, string DisplayName, string Description,
    string SystemPrompt, string Provider, string Model, int? MaxTokens,
    AdvisorMode Mode, AdvisorTrigger Trigger, bool IsSystem);

public enum AdvisorMode    { Strategic, Critical, DevilsAdvocate, DomainExpert }
public enum AdvisorTrigger { BeforeFirstExecution, BeforeEveryExecution, OnConvergenceFailure }
```

### Trigger-Typen

| Trigger | Bedeutung |
|---|---|
| `BeforeFirstExecution` | Advisor wird einmalig vor Iteration 1 konsultiert. Geeignet für strategische Briefing-Analyse. |
| `BeforeEveryExecution` | Advisor wird vor jeder Iteration konsultiert. Geeignet für kritische Gegenstimmen. |
| `OnConvergenceFailure` | Advisor wird nur bei Convergence-Failure konsultiert; danach folgt ein einmaliger Retry-Durchlauf. |

### System-Advisors

Modelle seit D-066 aus der Modellmatrix: `briefing-clarifier` und `crew-design-advisor` sind StrategicAdvisor (Fable), `devils-advocate` ist CriticalAdvisor (Fable), `legal-domain-expert` ist DomainExpertAdvisor/Legal (Opus, das als Executor-Modell entfällt, deshalb zur Laufzeit Astra), `academic-rigor-advisor` ist DomainExpertAdvisor/Academic (Astra).

| Name | Mode | Trigger | Zweck |
|---|---|---|---|
| `briefing-clarifier` | Strategic | BeforeFirstExecution | Analysiert das Briefing vor dem ersten Executor-Pass und liefert strukturierte Klärungshinweise. |
| `devils-advocate` | DevilsAdvocate | BeforeEveryExecution | Hinterfragt vor jeder Iteration die geplante Executor-Richtung kritisch, um Fehler durch blinden Fortschritt zu vermeiden. |
| `legal-domain-expert` | DomainExpert | BeforeFirstExecution | Domänen-Input für juristische Texte (Template `juristisch`). |
| `academic-rigor-advisor` | Critical | BeforeEveryExecution | Wissenschaftliche Strenge/Argumentationsqualität (Template `akademisch`). |

### Pipeline-Integration via Decorator

Der `AdvisorAwareExecutor` (in `Infrastructure/Pipeline/`) dekoriert `IExecutionStep` und schiebt sich transparent vor jeden Executor-Aufruf:

```
AdvisorAwareExecutor.ExecuteAsync(context)
  1. Filtert Advisors nach aktivem Trigger (BeforeFirst nur bei Iteration 1, BeforeEvery immer)
  2. Ruft ProfileBasedAdvisor für jeden passenden Advisor sequenziell auf
  3. Schreibt Output als "[ADVISOR: <name>]\n<text>" in context[AtelierContextKeys.AdvisorBlock]
  4. Persistiert AdvisorConsultation-Record (Tabelle AdvisorConsultations)
  5. Delegiert an den echten IExecutionStep
```

`AtelierPipelineFactory.BuildWithAdvisorContext(snapshot, context)` wired den Decorator und stellt sicher, dass der Advisor-Block im `IRunContext` propagiert wird.

### Advisor-Failure-Verhalten

Advisor-LLM-Calls sind nicht best-effort. Eine Exception in `ProfileBasedAdvisor` bubbled durch `AdvisorAwareExecutor` und bricht den Run mit `Status=Failed` ab (D-031(c)). Stiller Weiterlauf würde einen möglicherweise korrumpierten Kontext maskieren.

### Convergence-Failure-Retry-Mechanismus

```
Pipeline → ConvergenceFailedException
  → RunOrchestratorService.TryConvergenceFailureRetryAsync
      1. Prüft RunEntity.AdvisorRetryAttempted — true → eskaliert zu Failed (kein zweiter Retry)
      2. Setzt AdvisorRetryAttempted = true in DB
      3. Aktiviert OnConvergenceFailure-Advisors im nächsten Run-Kontext
      4. Startet Pipeline-Durchlauf erneut (einmalig)
      5. Zweites ConvergenceFailedException → Failed (kein weiterer Retry)
```

**Single-Retry-Cap:** `RunEntity.AdvisorRetryAttempted` (Migration Step11) verhindert Endlos-Schleifen. Multi-Retry mit konfigurierbarer Wiederholungsanzahl ist als Future Work dokumentiert.

### DB-Tabellen (Migration Step11AdvisorSystem)

| Tabelle | Inhalt |
|---|---|
| `AdvisorProfiles` | Custom Advisor-Profile (System-Advisors leben als Code-Konstanten in `SystemCrew`). |
| `AdvisorConsultations` | Persistierte Advisor-Outputs pro Iteration und Advisor (RunId, IterationNumber, AdvisorName, OutputText, CreatedAt). |

Spalte `RunEntity.AdvisorRetryAttempted` (bool, nullable) auf `Runs`-Tabelle.

### UI-Komponenten (PS-7)

| Komponente | Zweck |
|---|---|
| `AdvisorPicker` | Available/Selected-Liste analog `ReviewerPicker`, mit Trigger-Anzeige |
| `AdvisorConsultationsBlock` | Klappsection auf RunDetail-Page: zeigt alle Consultations pro Iteration |
| `AdvisorProfilesIndex` | Liste aller Advisor-Profile (System + Custom) unter `/crew/profiles/advisors` |
| `AdvisorProfileEditor` | CRUD-Editor für Custom Advisor-Profile |

`ProfileEditorForm` wurde um `ShowAdvisorFields` + Mode/Trigger Radio-Groups erweitert (wiederverwendbar für Reviewer, Executor und Advisor).

### MCP-Tool

`list_advisor_profiles` — listet alle Advisor-Profile (System + Custom).

## Finalizer-Profile (Step22 / D-044)

Finalizer sind Nachverarbeitungs-Akteure, die **nach** Abschluss der GEEF-Convergence-Schleife ausgeführt werden (oder, optional, wenn diese scheitert). Sie transformieren oder exportieren den Abschluss-Draft und erzeugen dabei `RunArtifact`-Datensätze.

### FinalizerProfile-Schema

```csharp
public sealed record FinalizerProfile(
    string Name, string DisplayName, string Description,
    FinalizerType FinalizerType, Dictionary<string, string> Settings,
    bool IsSystem, DateTime CreatedAt, DateTime UpdatedAt);

public enum FinalizerType
{
    FileExport    = 0,
    MetadataEnrich = 1,
    ExternalSink  = 2,
    Transform     = 3,
}
```

`FinalizerType` ist **nach der Erstellung unveränderlich**. Typisierte Settings-Records (`FileExportSettings`, `MetadataEnrichSettings`, `WebhookSinkSettings`, `EmailSinkSettings`, `TransformSettings`) kapseln das `Dictionary<string,string> Settings` für typsicheren Zugriff.

### Pipeline-Position

Finalizer laufen sequenziell in der in `CrewTemplate.FinalizerProfileNames` festgelegten Reihenfolge, nachdem die Convergence-Schleife beendet wurde. Das Flag `CrewTemplate.RunFinalizersOnMaxAttempts` steuert, ob Finalizer auch bei einem Convergence-Failure (Max-Attempts überschritten) ausgeführt werden.

Seit D-067:

- `Runs.Outcome` wird erst gesetzt, wenn die Finalizer fertig sind und der (gegebenenfalls umgewandelte) endgültige Text abgelegt ist; `Outcome` bedeutet also „fertig abgelegt, inklusive Finalizer“ ([02-architecture](02-architecture_de.md#lauf-ergebnis-outcome-und-runstatus)).
- Ein Finalizer, der fehlschlägt, übersprungen oder nicht ausgeführt wird (Best-Effort-Lauf ohne `RunFinalizersOnMaxAttempts`), wird zur Warnung am Lauf (`finalizer.failed`, `finalizer.skipped`, `finalizer.not_run`), statt still durchzugehen.
- Ein Abbruch oder Dienststopp während der Finalizer ist kein Fehler eines Finalizers: Der Lauf endet mit `Outcome = Aborted` und bekommt keine Warnung `finalizer.failed`. Die Ergebnisse der Finalizer, die vor dem Stopp fertig waren, bleiben erhalten (geänderter Text, Artefakte, Kosten); die übrigen laufen nicht mehr. Der Status eines konvergierten Laufs bleibt `Completed`; maßgeblich ist `Outcome`.
- Wird der Dienst während der Finalizer hart beendet, bekommt der Lauf beim nächsten Start ein Ergebnis und die Warnung `run.restarted_while_finishing` (Quelle `run`).
- Lässt sich das Ergebnis nicht vollständig speichern, bekommt der Lauf die Warnung `run.result_not_saved` (Quelle `run`) und `Outcome = Failed`.

### RunArtifact-Entity

Jede Finalizer-Ausführung hinterlässt einen `RunArtifact`-Datensatz:

| Feld | Typ | Beschreibung |
|---|---|---|
| `Id` | Guid | Primärschlüssel |
| `RunId` | Guid | FK → `Runs` |
| `FinalizerProfileName` | string | Name des erzeugenden Finalizers |
| `ArtifactType` | enum `{File, Url, Status}` | Art der Speicherung |
| `Filename` | string? | Dateiname (für `File`-Artefakte) |
| `ContentType` | string? | MIME-Typ |
| `SizeBytes` | long? | Dateigröße in Bytes |
| `StorageUri` | string | Speicherpfad oder URL |
| `StatusMessage` | string? | Lesbarer Status (für `Status`-Artefakte) |
| `CreatedAt` | DateTime | Erstellungszeitpunkt |

### System-Finalizer-Profile (17)

| Name | Typ | Beschreibung |
|---|---|---|
| `export-markdown` | FileExport | Exportiert den Abschluss-Draft als Markdown-Datei |
| `export-html` | FileExport | Exportiert den Abschluss-Draft als HTML-Datei |
| `export-pdf` | FileExport | Exportiert den Abschluss-Draft als PDF-Datei |
| `export-docx` | FileExport | Exportiert den Abschluss-Draft als DOCX-Datei |
| `export-txt` | FileExport | Exportiert den Abschluss-Draft als Plaintext-Datei |
| `export-json` | FileExport | Exportiert das Run-Ergebnis als strukturierte JSON-Datei |
| `add-front-matter` | MetadataEnrich | Fügt YAML-Front-Matter mit Run-Metadaten voran |
| `add-word-count-footer` | MetadataEnrich | Hängt eine Wortzahl-Fußzeile an den Draft |
| `add-reading-level` | MetadataEnrich | Hängt eine Lesbarkeits-Annotation (Flesch–Kincaid) an |
| `webhook-sink` | ExternalSink | Sendet das Artefakt per POST an eine konfigurierte Webhook-URL |
| `email-sink` | ExternalSink | Versendet das Artefakt als E-Mail-Anhang |
| `anti-ai-voice` | Transform | Schreibt den Draft um, um erkennbare KI-Formulierungen zu reduzieren |
| `tone-formalization` | Transform | Hebt das Register des Drafts auf formell/akademisch an |
| `tone-casual` | Transform | Senkt das Register des Drafts auf konversationell ab |
| `executive-summary` | Transform | Erzeugt eine prägnante Executive-Summary und stellt sie voran |
| `key-takeaways` | Transform | Hängt eine Bullet-Point-Zusammenfassung der wichtigsten Punkte an |
| `glossary` | Transform | Hängt ein Glossar fachspezifischer Begriffe an |

### DB-Tabellen (Migration Step22)

| Tabelle | Inhalt |
|---|---|
| `FinalizerProfiles` | Custom-Finalizer-Profile (System-Profile leben als Code-Konstanten in `SystemCrew`). |
| `RunArtifacts` | Ein Datensatz pro Finalizer-Output pro Run (siehe RunArtifact-Entity oben). |
| `FinalizationActorCosts` | Kosten-Datensätze pro Run und Finalizer für LLM-gestützte Transforms. |

Neue Spalten in bestehenden Tabellen:

| Tabelle | Spalte | Typ | Beschreibung |
|---|---|---|---|
| `CrewTemplates` | `FinalizerProfileNames` | JSONB | Geordnete Liste der Finalizer-Profilnamen |
| `CrewTemplates` | `RunFinalizersOnMaxAttempts` | boolean | Finalizer auch bei Convergence-Failure ausführen |
| `Runs` | `FinalizerCostEur` | numeric | Gesamt-LLM-Kosten der Finalizer für diesen Run |
| `Runs` | `FinalizerErrorMessage` | text | Fehlermeldung, falls ein Finalizer fehlgeschlagen ist |

### UI-Komponenten (Step22)

| Komponente | Zweck |
|---|---|
| `FinalizerPicker` | Available/Selected-Liste für Finalizer-Profile im `CrewTemplateEditor` |
| `FinalizerProfilesIndex` | Liste aller Finalizer-Profile (System + Custom) unter `/crew/profiles/finalizers` |
| `FinalizerProfileEditor` | CRUD-Editor für Custom-Finalizer-Profile |
| `FinalizerProfileView` | Read-only-Ansicht für System-Finalizer-Profile |
| `RunArtifactsTable` | Klappsection mit Artefakten auf der RunDetail-Page |

Der `CrewTemplateEditor` wurde um einen `FinalizerPicker` und den `RunFinalizersOnMaxAttempts`-Toggle erweitert.

### MCP-Tools (Step22)

- `list_run_artifacts` — listet alle Artefakte, die für einen bestimmten Run erzeugt wurden.
- `download_run_artifact` — lädt ein bestimmtes Run-Artefakt herunter (Owner-Check + Path-Containment erzwungen).

## API-Pfade

### Template-basierter Submit (Standard)

```csharp
await runService.SubmitRunAsync(
    briefingText: "...",
    configJson:   "{}",
    crewTemplateName: "klassik");  // null → Standard "klassik"
```

### Custom-Crew-Submit

```csharp
var spec = new CrewSpec(
    ExecutorProfileName:  "custom-my-executor",
    ReviewerProfileNames: ["briefing-fidelity", "custom-my-reviewer"],
    EvaluationStrategy:   EvaluationStrategy.Sequential,
    ConvergenceOverride:  new ConvergencePolicyOverride(MaxIterations: 3, null, null, null));

await runService.SubmitRunAsync("...", "{}", customCrew: spec);
```

### MCP-Tools

- `list_crew_templates` — listet alle Templates (System + Custom).
- `list_reviewer_profiles` — listet alle Reviewer-Profile (System + Custom).
- `list_advisor_profiles` — listet alle Advisor-Profile (System + Custom).
- `list_grounding_provider_profiles` — listet alle Grounding-Provider-Profile.
- `list_run_artifacts` — listet alle Artefakte, die für einen bestimmten Run erzeugt wurden.
- `download_run_artifact` — lädt ein bestimmtes Run-Artefakt herunter (Owner-Check + Path-Containment erzwungen).
- `submit_request` — erweitert um `crew_template` und `custom_crew` (JSON-String).

Vollständige Tool-Liste (15 Tools): siehe [09-endpoint-reference.md](09-endpoint-reference_de.md) und die [Projekt-README](../README_de.md).

## Grounding-Provider-Profile (D-036 / D-040 / D-051)

Grounding-Provider reichern das Briefing vor der GEEF-Ausführungsschleife mit externem Kontext an.

**Ist-Zustand (D-067):**

- Die fünf System-Profile `tavily-basic`, `tavily-refined`, `run-attachments`, `knowledge-base-default` und `academic-default` laufen über ihren unten genannten typspezifischen Provider, nicht über den Tool-Pfad, auch bei älteren Snapshots, die noch einen `ToolName` tragen. Nur eigene Profile mit `ToolName` nutzen den Tool-gestützten Provider ([10-tool-system](10-tool-system_de.md#a-t9--grounding-neuaufbau-auf-zentrale-tools)).
- Ein Provider, der übersprungen wird oder scheitert (fehlender Schlüssel, unbekannter Typ, Fehler), wird zur Warnung am Lauf (`grounding.provider_skipped`) statt zu still leerem Kontext. Der Code `grounding.tool_failed` ist reserviert und wird derzeit nie geschrieben; auch Fehler von Tool-Quellen laufen über `grounding.provider_skipped`.
- Schlüssel- und Header-Verweise (`authHeaderEnv`, `apiKeyEnv`) werden nur für Namen auf der Allowlist angenommen und gelesen ([10-tool-system](10-tool-system_de.md#secrets-und-die-allowlist-für-umgebungsvariablen)); der Grounding-Editor ist nur für Admins.

### Provider-Typen

| Typ | Implementierung | Beschreibung | Settings |
|---|---|---|---|
| `tavily` | `TavilyGroundingProvider` | Web-Suche via Tavily API (Basic oder Advanced). API-Key pro Profil. | `Tier` (basic/advanced), `MaxResults`, `IncludeAnswer` |
| `vector-store` | `VectorStoreGroundingProvider` | Semantische Suche in einer pgvector-Sammlung. Scope: `global` (nur gemeinsame Dokumente), `run-local` (Anhänge des Laufs und der Läufe, die er fortsetzt, nie die anderer Läufe) oder `both` (gemeinsame Dokumente plus diese Anhänge). Ein fehlender Scope bedeutet `global`; ein unbekannter Wert wird ebenfalls wie `global` behandelt (fail-closed, nie Anhänge anderer Läufe), und der Lauf bekommt die Warnung `grounding.scope_unknown` (D-067). `TagFilter` grenzt nur die gemeinsamen Dokumente ein; Anhänge haben keine Tags und werden davon nie ausgeblendet. | `TopK`, `Scope`, `TagFilter` |
| `static-context` | `StaticContextGroundingProvider` | Kuratierter Fixtext, der bei jedem Run unverändert injiziert wird. Keine externe API. Ideal für Style-Guides, Glossare, Markenstimme. | `label`, `content` (max 200.000 Zeichen, Soft-Limit 50.000) |
| `url-fetch` | `UrlFetchGroundingProvider` | Fetcht konkrete URLs, bereinigt HTML via HtmlAgilityPack, gibt Textinhalt zurück. SSRF-Guard blockiert private IPs. | `urls` (newline-separated), `maxContentPerUrl` (Default 8000), `stripBoilerplate` (bool, Default true) |
| `news-search` | `NewsSearchGroundingProvider` | Tavily-API mit `topic=news` + `days`-Filter. Für zeitkritische Themen. Attribution via `PublishedDate`. | `recencyDays` (Default 7), `newsMaxResults` (Default 5), `newsSearchDepth` (basic/advanced) |

### SSRF-Schutz (`url-fetch`)

Die `UrlSafetyValidator`-Komponente wird vor jedem HTTP-Request beim `url-fetch`-Provider ausgeführt:

- **Schema-Check:** Nur `http` und `https`. Alle anderen (`file://`, `ftp://`, custom) → blockiert.
- **DNS-Auflösung:** Jeder Hostname wird via `Dns.GetHostAddressesAsync` aufgelöst; **alle** resultierenden IPs werden geprüft (nicht nur die erste).
- **IPv4-Blockliste:** `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `127.0.0.0/8`, `169.254.0.0/16` (Link-Local + Cloud-Metadata), `0.0.0.0/8`, `100.64.0.0/10`, `224.0.0.0/4`
- **IPv6-Blockliste:** `::1`, `fc00::/7`, `fe80::/10`, `ff00::/8`, `64:ff9b::/96`, IPv4-mapped IPv6 (entpackt und erneut geprüft)
- **Redirect-Kette:** Max. 3 Hops; jede Redirect-Ziel-IP wird erneut gegen die vollständige Blockliste geprüft.
- **Timeout:** 10 Sekunden hard-cap pro Request; Response-Body auf 5 MB begrenzt.

Blockierte oder fehlgeschlagene URLs werden übersprungen (Warnung geloggt, in Source-Citation vermerkt); der Run selbst wird nicht abgebrochen.

### KI-Refinement

Jeder Grounding-Provider kann optional mit einem KI-Refinement-Pass konfiguriert werden. Nach dem Fetch läuft — wenn konfiguriert — ein LLM über die Rohergebnisse.

**Konfiguration** (flache Keys in `ProviderSettings`):

| Key | Typ | Beschreibung |
|---|---|---|
| `refinementProvider` | string | LLM-Anbieter (z. B. `openrouter`) |
| `refinementModel` | string | Modell (z. B. `google/gemini-2.0-flash-lite`) |
| `refinementMaxTokens` | int | Max. Token für Refinement-Antwort |
| `refinementTemperature` | double? | Optional; leer = Anbieter-Standard |
| `refinementMode` | int | `0` = Filter, `1` = Synthesize |
| `refinementInstructions` | string? | Optionale Zusatz-Anweisungen |

**Modi:**
- **Filter** (Standard): Jede Quelle wird einzeln behalten oder verworfen. Attribution bleibt 1:1 erhalten.
- **Synthesize**: Alle Quellen werden zu einem kohärenten Text zusammengefasst (`[n]`-Referenzen). Originalquellen bleiben als Referenz-Anhang erhalten.

**Graceful Degradation:** Ist der Refinement-Anbieter inaktiv oder schlägt der LLM-Call fehl, werden die Rohergebnisse unverändert durchgereicht. Der Run wird nicht abgebrochen. Die Grounding-Visualisierung zeigt einen Hinweis.

**System-Profil `tavily-news`:** Neu — Tavily-Newssuche (`topic=news`, `recencyDays=7`) mit Filter-Refinement via `google/gemini-2.0-flash-lite`. Geeignet für zeitkritische Themen.

**System-Profil `tavily-refined`:** Sofort nutzbares Demo-Profil — Tavily Advanced mit Filter-Refinement via `google/gemini-2.0-flash-lite`.

## Reviewer-Name-Migration

| Alt (pre-PS-5) | Neu (PS-5) |
|---|---|
| `BriefingTreueReviewer` | `briefing-fidelity` |
| `KlarheitReviewer` | `clarity` |

Migration Step10 benennt historische `Findings.ReviewerName`-Werte um. `ReviewerDisplay.ToDisplay()` enthält beide Varianten als Fallback.

## Systemtrennung (Namespace)

- `Core/Domain/Crew/` — alle Domain-Records (keine Infrastruktur-Abhängigkeit).
- `Core/Domain/Crew/SystemPrompts.cs` — System-Prompt-Texte (lang, gehören semantisch zu System-Profilen).
- `Infrastructure/Pipeline/ProfileBasedReviewer.cs` / `ProfileBasedExecutor.cs` — Geef-SDK-Adapter.
- `Application/Crew/CrewService.cs` + `CrewSnapshotBuilder.cs` — orchestriert Repo-Lookups + Snapshot-Konstruktion.

## PS-6 — UI-Pfade und Konventionen

### Routing-Map

| URL | Komponente | Beschreibung |
|---|---|---|
| `/crew` | `CrewIndex` | Landing-Page mit Überblick über Templates + Profile |
| `/crew/templates` | `CrewTemplatesIndex` | Liste aller Templates (System + Custom) |
| `/crew/templates/new` | `CrewTemplateEditor` | Neues Template anlegen |
| `/crew/templates/{name}` | `CrewTemplateEditor` | Template bearbeiten / System-Template duplizieren |
| `/crew/profiles/reviewers` | `ReviewerProfilesIndex` | Liste aller Reviewer-Profile |
| `/crew/profiles/reviewers/new` | `ReviewerProfileEditor` | Neues Reviewer-Profil anlegen |
| `/crew/profiles/reviewers/{name}` | `ReviewerProfileEditor` | Reviewer-Profil bearbeiten |
| `/crew/profiles/executors` | `ExecutorProfilesIndex` | Liste aller Executor-Profile |
| `/crew/profiles/executors/new` | `ExecutorProfileEditor` | Neues Executor-Profil anlegen |
| `/crew/profiles/executors/{name}` | `ExecutorProfileEditor` | Executor-Profil bearbeiten |
| `/crew/profiles/advisors` | `AdvisorProfilesIndex` | Liste aller Advisor-Profile (System + Custom) |
| `/crew/profiles/advisors/new` | `AdvisorProfileEditor` | Neues Advisor-Profil anlegen |
| `/crew/profiles/advisors/{name}` | `AdvisorProfileEditor` | Advisor-Profil bearbeiten |
| `/crew/profiles/grounding-providers` | `GroundingProviderIndex` | Liste aller Grounding-Provider-Profile |
| `/crew/profiles/finalizers` | `FinalizerProfilesIndex` | Liste aller Finalizer-Profile (System + Custom) |
| `/crew/profiles/finalizers/create` | `FinalizerProfileEditor` | Custom-Finalizer-Profil anlegen |
| `/crew/profiles/finalizers/edit/{name}` | `FinalizerProfileEditor` | Custom-Finalizer-Profil bearbeiten |
| `/crew/profiles/finalizers/view/{name}` | `FinalizerProfileView` | System-Finalizer-Profil ansehen (read-only) |
| `/crew/studio` | `TemplateStudio` | KI-gestützter Template-Wizard (Analyse → Review → Edit → Materialisierung) |

### UI-Komponenten

| Komponente | Ort | Zweck |
|---|---|---|
| `CrewBadge` | `Components/UI/` | Dezenter Text-Badge mit Template-Namen in RunRow |
| `CrewSelector` | `Components/UI/` | Dropdown zur Template-Auswahl auf der NewRun-Page |
| `CrewSummary` | `Components/UI/` | Click-to-Expand Crew-Übersicht auf RunDetail-Page |
| `ReviewerPicker` | `Components/UI/` | Available/Selected-Liste mit Up/Down-Reordering |
| `ProfileEditorForm` | `Components/UI/` | Generisches Form für Reviewer- und Executor-Profile |
| `Modal` | `Components/UI/` | Generische Modal-Komponente mit Backdrop |
| `DeleteConfirmationModal` | `Components/UI/` | Bestätigungs-Modal: User muss Namen tippen |

### Name-Constraints

Pattern `^[a-z0-9\-]+$`, max 64 Zeichen — gilt für alle Profile- und Template-Namen (Custom-Prefix exkl.). Form-Validierung via `DataAnnotations.RegularExpression`. Service-Layer ist idempotent bzgl. `custom-`-Prefix.

## Template Studio (D-043)

Das Template Studio unter `/crew/studio` ist ein KI-gestützter Wizard, der für eine beschriebene Aufgabe eine vollständige Crew-Konfiguration vorschlägt. Der Nutzer kann jeden Vorschlag im Edit-Step prüfen und bearbeiten, bevor er in die DB materialisiert wird.

### Wizard-Schritte

| Schritt | Komponente | Beschreibung |
|---|---|---|
| TaskInput | `StudioTaskInputStep` | Freitext-Aufgabenbeschreibung; löst LLM-Analyse aus |
| Analyzing | `StudioAnalyzingStep` | Lade-Indikator während das Meta-LLM läuft |
| Review | `StudioReviewStep` | Zeigt den KI-Vorschlag; Option, ein bestehendes Template zu verwenden |
| Edit | `StudioEditStep` | Vollständiger Editor für das vorgeschlagene Template und alle Profile |
| Confirmation | `StudioConfirmationStep` | Zeigt Materialisierungs-Ergebnis; startet einen Run |

### Analyse-Pipeline (`AnalyzeAsync`)

`TemplateStudioService.AnalyzeAsync` verwandelt die Freitext-Aufgabenbeschreibung in eine persistierte `TemplateStudioAnalysis`, die den Review- und Edit-Step speist. Die Pipeline:

1. **Modell-Auflösung** — wählt das Meta-LLM in der Reihenfolge: expliziter Pro-Analyse-Override → persistierter Studio-Default (`StudioSettings`) → `appsettings`-Default (`TemplateStudioOptions`).
2. **Kontext-Aufbau (parallel, budgetiert)** — lädt alle Crew-Listen (Templates, Executors, Reviewer, Advisors, Grounding-Provider, Finalizer) und ruft die Modell-Kataloge aller Provider **parallel** unter einem 20-Sekunden-Budget ab. Ein langsamer oder nicht erreichbarer Provider liefert keine Modelle, statt die gesamte Analyse zu blockieren; empfohlene Modelle werden zuerst gelistet, damit das LLM gültige IDs wählt.
3. **Meta-LLM-Aufruf (Timeout-gedeckelt)** — ruft das Modell mit dem über `tool_choice` erzwungenen Tool `submit_template_proposal` auf. Ein Hard-Cap (`TemplateStudioOptions.AnalysisTimeoutSeconds`) wandelt einen hängenden Provider in eine `TimeoutException` mit Retry-Hinweis. Eine Antwort ohne Tool-Aufruf löst eine `InvalidOperationException` aus.
4. **Vorschlags-Parsing** — `ParseProposal` liest das Tool-Call-JSON in `MatchedExistingTemplates`, eine `StudioRecommendation` (`use_existing` / `adapt_existing` / `create_new`), das `ProposedTemplate` sowie die Liste der `ProposedProfile`-Records (jeweils mit feldbezogenen LLM-Begründungen).
5. **Defaults & Clamping** — `ApplyDefaults` füllt leere Provider/Modell/MaxTokens pro Profiltyp aus `StudioDefaults`. `ClampMaxTokens` erzwingt den `MinMaxTokens`-Floor für generierende Profile (Executor, Reviewer, Advisor) und setzt `null` für Grounding-/Finalizer-Profile (keine eigene LLM-Generierung).
6. **Deduplizierung** — `ProfileSimilarityService.FindSimilarAsync` verwirft vorgeschlagene Profile, die einem bestehenden zu ähnlich sind (Ähnlichkeit über Name + Prompt, `TemplateStudioOptions.SimilarityThreshold`).
7. **Kosten & Persistierung** — Input-/Output-Tokens werden über `IPricingCatalog` bepreist; die vollständige Analyse (inkl. Kosten in EUR) wird über `ITemplateStudioAnalysisRepository` gespeichert und zurückgegeben. `ListRecentAnalysesAsync` stellt die Historie bereit (siehe `StudioAnalysisHistoryList`).

### StudioEditStep — Feld-Parität (D-043)

Der Edit-Step exponiert das vollständige Feld-Set für das Template und jeden Profil-Slot:

**Template-Felder:** DisplayName, Description, EvaluationStrategy (Dropdown), EvaluationStrategyReasoning (read-only, vom LLM)

**Pro Profil-Slot (Executor / Reviewer × N / Advisor × N / GroundingProvider × N / Finalizer × N):**
- **UseExisting / CreateNew Toggle** — bestehendes Profil per Name wählen oder neues Profil inline konfigurieren
- **CreateNew-Felder:** Name (kebab-case), DisplayName, Description, Provider, Modell (`ModelSelector`), MaxTokens, System-Prompt
- **Reviewer-spezifisch:** ReviewerFocus (optional)
- **Advisor-spezifisch:** AdvisorMode (Strategic / Critical / DevilsAdvocate), AdvisorTrigger (BeforeFirstExecution / BeforeEveryExecution / OnConvergenceFailure)
- **GroundingProvider-spezifisch:** GroundingProviderType (Tavily / VectorStore), Typ-spezifische Einstellungen (API-Key oder Collection-Name)
- **Finalizer-spezifisch:** FinalizerType (FileExport / MetadataEnrich / ExternalSink / Transform), Typ-spezifische Einstellungen
- **Reasoning-Anzeige:** LLM-Begründungen pro Feld, read-only (aus `analyze_template_proposal`)
- **Field-Helps:** Englische Inline-Hinweise für jedes Feld (`StudioFieldHelps.cs`)

### Schlüssel-Komponenten

| Komponente | Zweck |
|---|---|
| `StudioProfileSlot.razor` | UseExisting/CreateNew-Toggle + vollständiges Inline-Profil-Form; bettet `ModelSelector` ein |
| `FieldHelp.razor` | Inline-Hinweis unterhalb jedes Feldes |
| `StudioFieldHelps.cs` | Zentrale englische Field-Help-Text-Konstanten |

### Materialisierung (atomar, D-043/7)

`TemplateStudioService.MaterializeAsync` kapselt alle DB-Schreibvorgänge in einer einzelnen EF-Core-Transaktion (`IAtomicTransactionFactory`) und gibt ein `MaterializationResult` zurück (finaler Template-Name, angelegte Profil-Namen, Warnungen).

**Validierung vor der Transaktion:**
- `ValidateNotSystemProfiles` — vorgeschlagene Profil-Namen dürfen nicht mit system-reservierten Namen kollidieren.
- `ValidateReviewerCount` — das Template muss mindestens einen Reviewer tragen.
- `ValidateAvailabilityAsync` — prüft das Modell jedes generierenden Profils gegen den Provider-Katalog; eine Abweichung erzeugt eine **nicht-blockierende Warnung** (das Modell fehlt evtl. nur im Live-Katalog), keinen Abbruch.

**Transaktions-Ablauf:** Begin → Profile anlegen (Executor, Reviewer, Advisor, GroundingProvider, Finalizer) via `CreateProfileAsync` → Template anlegen via `CreateTemplateAsync` → `MarkMaterializedAsync` (markiert den Analyse-Datensatz als verbraucht) → Commit. Jeder Fehler löst ein explizites Rollback aus — kein halb-materialisierter Zustand.

**Name-Mapping:** `CreateCustom*Async` präfixiert jeden neuen Profil-Namen idempotent mit `custom-`. `MaterializeAsync` merkt sich das Mapping alt→final, und `ApplyProfileNameMapping` schreibt alle Template-Referenzen (Executor, Reviewer, Advisors, Grounding-Provider, Finalizer) auf die tatsächlich gespeicherten Namen um, bevor das Template angelegt wird. Namen, die bereits auf bestehende Profile zeigen, bleiben unverändert. Die Evaluation-Strategie wird normalisiert (`NormalizeEvaluationStrategy`, Default `Sequential`).

Finalizer-Vorschläge erscheinen in der LLM-Analyse-Ausgabe des Studios; `CreateProfileAsync` behandelt den Finalizer-Zweig; `StudioEditStep` stellt den Finalizer-Slot-Abschnitt neben den übrigen Profil-Slots bereit.

Seit D-067 entfernt das Studio `authHeaderEnv`/`apiKeyEnv` mit einer Warnung aus vorgeschlagenen Grounding-Profilen, und sein Prompt bietet diese Schlüssel nicht mehr an; Verweise auf Geheimnisse setzen nur Administratoren im Grounding-Editor.

## Kontinuierlicher Lernzyklus (D-054)

> **Seit D-067 pausiert.** Die Schleife läuft nur, wenn `Learning:Enabled` auf `true` steht (Standard
> `false`). Solange sie aus ist, kehren `learning-extractor` und `learning-publisher` sofort zurück (kein
> LLM-Aufruf, kein Eintrag, kein `learning-evaluation`-Lauf, kein Embedding), `learning-retrieval` liefert
> leeren Kontext, und `/crew/learnings` zeigt, dass Learning pausiert ist. Crews mit diesen Teilen bleiben
> gültig. Die Schleife soll durch ein Gedächtnis je Nutzer ersetzt werden (D-068).

### Architektur

```
NORMALER RUN  →  Finalizer „learning-extractor" (opt-in)
   ├─ Guard: run.Kind == Learning → return       (Rekursions-Stopp #1)
   ├─ Schwelle: ≥ 2 Iterationen ODER Major+-Finding
   ├─ Strukturierte Fakten → LearningEntry (Proposed)
   └─ FIRE-AND-FORGET: SubmitRunAsync(crew=learning-evaluation, Kind=Learning)

LEARNING-RUN  (Kind=Learning, Crew „learning-evaluation")
   ├─ Executor kondensiert Kandidat
   ├─ 3 strenge Reviewer (AbortOnCritical=true, MaxIterations=2)
   └─ Finalizer „learning-publisher" (RunFinalizersOnMaxAttempts=true)
        ├─ Guard: run.Kind != Learning → return   (Rekursions-Stopp #2)
        ├─ Konvergenz  → Embedding berechnen, Approved, in Store schreiben
        └─ Nicht-Konv. → Rejected, nichts geschrieben

SPÄTERER RUN  →  Grounding „learning-retrieval"
   ├─ Embedding-Suche über Approved-Learnings
   ├─ Domänen-Boost: finalScore = similarity × (sameDomain ? boost : penalty)
   ├─ Kuratiertes Wissen (vector-store) schlägt Learnings per Provider-Reihenfolge
   └─ SourceCitation: learning://{id}
```

### `RunKind`-Enum

`Standard = 0` (Standard) / `Learning = 1` / `CrewComposition = 2`. In `RunEntity.Kind` geführt, durch `SubmitRunRequest.Kind` und `IRunPersistenceService.CreateRunAsync` durchgereicht. Der Orchestrator dispatcht alle drei Arten identisch; das Kind gatet Finalizer-Wächter und Rekursionsstopps.

### `LearningEntry`-Lebenszyklus

`Proposed` (Extractor) → `Approved` (Publisher + Embedding) oder `Rejected` (Publisher, nichts gespeichert). Manuelles Override über `/crew/learnings` (Owner-Check).

### System-Profile (Migration Step30)

| Name | Typ | Hinweis |
|---|---|---|
| `learning-extractor` | Finalizer (LearningExtract) | Standard in allen vier Standard-Templates (D-057); nicht in `learning-evaluation` (Rekursionsstopp) |
| `learning-publisher` | Finalizer (LearningPublish) | Teil der `learning-evaluation`-Crew |
| `learning-evaluation` | CrewTemplate | AbortOnCritical=true, MaxIterations=2 |
| `learning-retriever-default` | GroundingProvider (LearningRetrieval) | sameDomainBoost=1.0, crossDomainPenalty=0.5 |
| `learning-factual-grounding` | Reviewer | codex-cli/gpt-5.6-sol |
| `learning-value` | Reviewer | openrouter/gemini-3.1-pro-preview |
| `learning-generalizability` | Reviewer | claude-cli/claude-opus-4.8 |

## Auto-Crew: Kompositions-Run

Ein Kompositions-Run (`RunKind.CrewComposition = 2`) ist ein vollständiger GEEF-Run, der die GEEF-Evaluierungsschleife nutzt, um eine neue Crew zu komponieren. GEEF-on-GEEF: die Meta-Crew `crew-composer` durchläuft Draft → Critique → Refine → Converge und materialisiert das Ergebnis anschließend über den `crew-materializer`-Finalizer. Siehe D-059 im [Decisions-Log](05-decisions-log_de.md).

### Was ein Kompositions-Run ist

Der Single-Pass-Meta-LLM-Aufruf des Template Studios (D-038) produzierte Crews ohne Selbstkorrektur. Ein Kompositions-Run ersetzt diesen Aufruf durch eine echte GEEF-Pipeline: die Crew `crew-composer` verfeinert ein `CrewSpecArtifact`-JSON iterativ bis zur Konvergenz; danach wandelt der `CrewMaterializeFinalizerExecutor` das konvergierte Spec in echte DB-Datensätze um.

Einstiegspunkt: `/crew/studio` (eigenständige Komposition, `ChainToTaskRun = false` als Standard). Der Nutzer gibt eine Aufgabenbeschreibung ein; das Studio reicht einen `RunKind.CrewComposition`-Run ein und zeigt den Fortschritt via SignalR.

### Die 5 Reviewer von crew-composer

| Name | Typ | Provider / Modell | Rolle |
|---|---|---|---|
| `crew-spec-validator` | **Deterministisch** (kein LLM) | — | Prüft das `CrewSpecArtifact`-JSON-Schema in der Schleife; fehlende Pflichtfelder oder ungültige Struktur → Critical-Finding, kein LLM-Aufruf nötig |
| `crew-diversity-reviewer` | LLM | codex-cli / gpt-5.6-sol | Prüft Modell-Pluralismus: Executor und Reviewer müssen ≥ 2 verschiedene Provider abdecken |
| `crew-prompt-quality-reviewer` | LLM | claude-cli / claude-opus-4-8 | Bewertet Qualität, Spezifität und Rollenklarheit jedes System-Prompts |
| `crew-grounding-fit-reviewer` | LLM | codex-cli / gpt-5.6-sol | Beurteilt, ob die gewählten Grounding-Provider zur Aufgabendomäne passen |
| `crew-finalizer-fit-reviewer` | LLM | codex-cli / gpt-5.6-sol | Prüft, ob die gewählten Finalizer zu den Output-Anforderungen der Aufgabe passen |

Der deterministische `CrewSpecValidatorReviewer` erzeugt Findings ohne LLM-Call und ist das primäre strukturelle Gate. Die vier LLM-Reviewer laufen in der `Parallel`-Strategie.

**ConvergenceOverride:** `MaxIterations = 4`, `AbortOnCritical = false` (damit Validator-Critical-Findings korrigiert werden können, statt einen Abbruch auszulösen), `StagnationThreshold = 3`.

### `CrewSpecArtifact`-Schema

Der `CrewComposerExecutor` erzwingt den Tool-Call `submit_crew_spec` — kein Freitext-Output. Das resultierende JSON folgt diesem Schema:

```json
{
  "mode": "existing-template | composed | new",
  "reuse": "<Template-Name oder null>",
  "executor": {
    "name": "...", "provider": "...", "model": "...",
    "systemPrompt": "...", "maxTokens": null
  },
  "reviewers": [
    { "name": "...", "provider": "...", "model": "...",
      "systemPrompt": "...", "focus": "..." }
  ],
  "advisors": [
    { "name": "...", "mode": "Strategic|Critical|DevilsAdvocate|DomainExpert",
      "trigger": "BeforeFirstExecution|BeforeEveryExecution|OnConvergenceFailure",
      "systemPrompt": "..." }
  ],
  "grounding": ["<Profilname>", "..."],
  "finalizers": ["<Profilname>", "..."],
  "evaluationStrategy": "Parallel | Sequential | FailFast | Priority",
  "convergenceOverride": null
}
```

**Modus-Semantik:**
- `existing-template` — bestehendes Template per Name wiederverwenden (`reuse`-Feld); keine DB-Schreibvorgänge.
- `composed` — neues Template auf Basis des `reuse`-Templates mit Änderungen zusammensetzen.
- `new` — vollständig neues Template aus den Spec-Feldern anlegen; alle Profile müssen vollständig spezifiziert sein.

Das `reuse`-Feld kodiert das **Dedup-Ergebnis**: findet `CrewMaterializeFinalizerExecutor` in `CrewTemplateEmbeddings` ein Template mit Cosine-Similarity ≥ 0,90, wird der Modus auf `existing-template` downgegradet und `reuse` auf den gematchten Template-Namen gesetzt.

### Schritte des `CrewMaterializeFinalizerExecutor`

Läuft nach Konvergenz des Kompositions-Runs (`FinalizerType.CrewMaterialize = 6`):

1. **Parse** — liest das `CrewSpecArtifact`-JSON aus dem `ArtifactText` der letzten Iteration.
2. **Validate** — strukturelle Prüfung: Pflichtfelder vorhanden, `reviewers`-Anzahl ≥ 1, Provider-Namen im System aktiv.
3. **Dedup** — berechnet ein Embedding des Specs, fragt `CrewTemplateEmbeddings` auf Cosine-Similarity ab; überschreitet ein bestehendes Template die 0,90-Schwelle, wird der Modus auf `existing-template` downgegradet (keine neuen DB-Datensätze).
4. **Materialize** — für Modi `composed` / `new`: legt Profile (Executor, Reviewer × N, Advisor × N, GroundingProvider × N, Finalizer × N) und das Crew-Template in einer einzigen EF-Core-Transaktion an (analog `TemplateStudioService.MaterializeAsync`).
5. **Embed** — berechnet ein Embedding des neuen Templates und schreibt es in `CrewTemplateEmbeddings` für künftige Dedup-Prüfungen.
6. **Task-Run verketten** — wenn `ChainToTaskRun = true` und der Kompositions-Run ein Seed-Briefing enthält, wird ein neuer `RunKind.Standard`-Run mit der materialisierten Crew und dem ursprünglichen Briefing eingereicht.

### `ParentCompositionRunId`-Audit-Link

Wird ein Task-Run aus einem Kompositions-Run verkettet, wird `Runs.ParentCompositionRunId` auf die ID des Kompositions-Runs gesetzt. Das ergibt einen vollständigen Audit-Trail: ausgehend von einem beliebigen Task-Run lässt sich über `ParentCompositionRunId` der Kompositions-Run nachvollziehen, der seine Crew erzeugt hat.

### Wächter (Guards)

- `LearningExtractFinalizerExecutor`: `if (run.Kind == RunKind.CrewComposition) return Ok;` — aus Kompositions-Runs werden keine Learnings extrahiert.
- `CrewMaterializeFinalizerExecutor`: der verkettete Run ist stets `RunKind.Standard`; verschachtelte Komposition ist nicht möglich.
- `CrewComposerExecutor`: nur für `Kind == CrewComposition` aktiv; andere Run-Arten werden abgewiesen.

## Hochgeladene Dateien sicher lesen (Zusatz zu D-069)

Dateien aus `POST /api/sources` und aus MCP-Anhängen laufen durch `SourceIntakeService`. PDF- und Word-Dateien liest
dieselbe Anwendung in einem **eigenen Prozess**, damit eine bösartige Datei den Webprozess nie beenden kann.

### Ablauf der Aufnahme

1. **Upload-Platz.** Höchstens `Sources:MaxConcurrentIntakes` Uploads laufen gleichzeitig; wer keinen Platz bekommt,
   wartet bis `Sources:ExtractionQueueTimeout`, sonst lautet die Quittung „The service is busy with other uploads.
   Please try again in a minute.“
2. **Streamen.** Der Upload wird nach `<StorageRoot>/.incoming/<id>/upload.part` geschrieben (Verzeichnis 0700,
   Datei 0600), unterwegs gehasht und bei `Sources:MaxFileBytes` begrenzt. Im Speicher bleiben nur die ersten 8 KiB
   für die Typerkennung.
3. **Wiederverwendung und Kontingent.** Eine gleiche Datei desselben Besitzers wird wiederverwendet (der eindeutige
   Index `UX_Sources_OwnerUsername_Sha256` sorgt dafür, dass parallele Uploads derselben Datei bei einer Quelle
   enden). Dateien, die kein Auftrag nutzt, zählen gegen `Sources:MaxUnattachedSourcesPerUser` (50) und
   `Sources:MaxUnattachedBytesPerUser` (500 MB).
4. **Extraktion.** Text und Markdown liest der Webprozess selbst. PDF und Word belegen einen Extraktionsplatz
   (`Sources:ExtractionMaxConcurrency`, Standard 2) und laufen als
   `dotnet <Anwendungs-Assembly> --extract-source <pdf|docx> <datei>`. `Program.cs` verzweigt in diesen Modus, bevor ein
   Host gebaut wird: kein DI, keine Datenbank, keine Konfiguration, keine Anmeldung.
5. **Seitenmarken.** Zeilen, die wie `[[page N]]` aussehen, werden in Text-, Markdown- und Word-Dateien sowie in jedem
   Stück einer vom Zeilenumbruch geteilten Zeile entschärft; echte Marken schreibt nur der PDF-Extraktor.
6. **Ablage.** Die empfangene Datei wird nach `<StorageRoot>/<Besitzer-Hash>/<id>.<endung>` verschoben; das
   temporäre Verzeichnis wird in jedem Fall entfernt.

### Grenzen des Extraktionsprozesses

| Grenze | Option | Standard | Wirkung |
|---|---|---|---|
| Gesamtzeit | `ExtractionTimeout` | 60 s | ganzer Prozessbaum wird beendet, „Reading this file took too long.“ |
| Verwalteter Speicher | `ExtractionMemoryLimitBytes` | 1 GiB | `DOTNET_GCHeapHardLimit` im Kind; ein Wächter beendet es zusätzlich oberhalb der Grenze plus 256 MiB |
| Antwortgröße | `ExtractionMaxOutputBytes` | 64 MiB | Prozess wird beendet, Datei unlesbar |
| Umgebung | — | — | geleert; nur `PATH`, `HOME`/`TMPDIR` (das private Upload-Verzeichnis), GC-Einstellungen und die Extraktionsgrenzen als JSON |
| Arbeitsverzeichnis | — | — | das private Upload-Verzeichnis |

Ein Absturz (etwa ein Stack Overflow, Exit-Code ab 128), ein Speicherende oder ein Abbruch endet als Quittung mit
„This file is too complex to read. Please save it again or split it into smaller files.“ Der Webprozess protokolliert
nur Art, Ergebnis, Dauer, Exit-Code und Spitzenspeicher, nie Dateiinhalte oder -namen.
`Sources:ExtractionMode = InProcess` gibt es nur für Tests der Extraktoren.

### Schutz in den Extraktoren

- **Word:** Archivprüfung (Einträge, Gesamtgröße `MaxUncompressedBytes` = 64 MiB, Kompressionsverhältnis ab 64 KiB
  Eintragsgröße), danach wird jeder XML-Teil einmal streamend ohne DTD und mit Tiefenlimit (`MaxXmlDepth` = 100)
  gelesen, bevor das Dokumentmodell entsteht; der Inhalt wird ohne Rekursion durchlaufen.
- **PDF:** Vor dem Öffnen werden die Flate-Ströme, die keine Bilder sind, ins Leere entpackt und aufsummiert; über
  `MaxPdfInflatedBytes` (256 MiB) wird die Datei abgelehnt. Verschachtelte Filter fangen die Prozessgrenzen ab.

### Aufräumen

Der stündliche Aufräumdienst löscht nach `Sources:OrphanFileAge` (24 h) zusätzlich liegengebliebene
Upload-Verzeichnisse, `*.part`-Dateien und abgelegte Dateien, auf die keine Quelle verweist. Er betrachtet nur die
Wurzel und die Besitzerordner darunter, überspringt Links und löst jeden Pfad innerhalb der Wurzel auf.

### Hinweis für den Betrieb

Der Container hat kein hartes Speicherlimit. Bei zwei Extraktionsplätzen mit je 1 GiB verwaltetem Speicher plus
Webprozess ist ein Limit von etwa 3–4 GiB für den Web-Container eine sinnvolle Obergrenze; die Entscheidung liegt beim
Betrieb.
