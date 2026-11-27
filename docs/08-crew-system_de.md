# 08 — Crew-System (PS-5)

*[English](08-crew-system.md) · **Deutsch***

Letzte Aktualisierung: 2026-11-26 (Hinweis, wo das Crew-System heute steht; D-076: Dateien über das Upload-Ticket, Aufräumen und Auswertung der Ablehnungen; zuvor 2026-11-25, D-075 Teil B: Nachbesserungslauf im Direktweg; D-074: Recherche im Grounding-Schritt des Direktwegs; zuvor 2026-11-24, D-067: strenges Lesen des Snapshots, mindestens ein Reviewer, Outcome nach den Finalizern, Warnungen am Lauf, System-Grounding wieder über den Provider-Typ-Weg, Learning standardmäßig pausiert; zweite Prüfrunde: Reviewer-Prüfung beim Speichern, Vektor-Scope `both`, `grounding.scope_unknown`, `grounding.tool_failed` reserviert, Finalizer und Abbruch; zuvor 2026-11-23, D-066/12: `model_domain` explizit, ein Zuweisungsprädikat; D-066/10–11: Codex-Aliase, Host-Abgleich, verfügbare Modelle nur laut Live-Katalog; zuvor 2026-11-22, D-066: Modellmatrix — System-Profile und Abschnitt Modellmatrix neu; zuvor 2026-06-04, D-059: Auto-Crew)

## Überblick

> **Wo das heute steht.** Das Crew-System ist die ältere Arbeitsweise. Seit dem Umbau im November 2026 ist der Auftrag mit dem
> Direktweg der Standard (D-068 bis D-077, [15-developer-guide_de.md](15-developer-guide_de.md)). Crews nutzen weiterhin „Extra
> careful“, die älteren MCP-Werkzeuge und die Werkstatt-Seiten, deren Menü standardmäßig nur die Administration sieht
> (`Ui:WorkshopForAdminsOnly`, [14-admin-guide_de.md](14-admin-guide_de.md)).

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

Alle vier Standard-Templates liefern drei Standard-Grounding-Provider (`tavily-refined`, `run-attachments`, `learning-retriever-default`) und einen Standard-Finalizer (`learning-extractor`) aus. Einzige Ausnahme ist das `learning-evaluation`-Template — es enthält weder `learning-retriever-default` noch `learning-extractor` (Rekursionsstopp). Seit D-067 laufen die Learning-Teile leer, solange `Learning:Enabled` nicht gesetzt ist (siehe [Kontinuierlicher Lernzyklus](#kontinuierlicher-lernzyklus-d-054)).

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
| Intake | Sonnet → Sol (Klärung eines Auftrags vor dem Schreiben, D-071; kein Profil darf diesen Slot tragen) |

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

**Composer-Settings:** Inline-Grounding und -Finalizer des Composers dürfen nur unkritische Einstellungen setzen (Whitelist `CrewPartSettingsPolicy`). Ziele, Zugangsdaten, Variablennamen und Modellbindungen werden verworfen und als Warnung gemeldet. Inline-Grounding eines nicht registrierten Typs ist kritisch. Seit Stufe 8c (D-089) kennt der Composer `rest-api`, `academic-search`, `news-search` und `ExternalSink` nicht mehr.

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
der Composer verlangt ebenfalls einen Reviewer.

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

Der in D-031(e) geplante einmalige Retry mit `OnConvergenceFailure`-Advisors wurde nie verdrahtet; `RunEntity.AdvisorRetryAttempted` wurde nie geschrieben. Seit Stufe 8c (D-089) ist die Eigenschaft aus dem Modell entfernt; die Spalte bleibt mit dem Standardwert `false` in der Datenbank.

### DB-Tabellen (Migration Step11AdvisorSystem)

| Tabelle | Inhalt |
|---|---|
| `AdvisorProfiles` | Custom Advisor-Profile (System-Advisors leben als Code-Konstanten in `SystemCrew`). |
| `AdvisorConsultations` | Persistierte Advisor-Outputs pro Iteration und Advisor (RunId, IterationNumber, AdvisorName, OutputText, CreatedAt). |

Spalte `AdvisorRetryAttempted` (bool, NOT NULL DEFAULT false) auf der `Runs`-Tabelle, nie geschrieben und seit D-089 nicht mehr zugeordnet.

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
    [Obsolete] ExternalSink = 2,   // in Stufe 8c ausgemustert (D-089)
    Transform     = 3,
}
```

`FinalizerType` ist **nach der Erstellung unveränderlich**. Typisierte Settings-Records (`FileExportSettings`, `MetadataEnrichSettings`, `TransformSettings`) kapseln das `Dictionary<string,string> Settings` für typsicheren Zugriff.

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

### System-Finalizer-Profile

`webhook-sink` und `email-sink` (`ExternalSink`) wurden in Stufe 8c entfernt (D-089); ihre Zeilen bleiben in der Datenbank, Listen lassen sie aus, und ein Lauf überspringt sie mit einer Warnung.

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

- Die fünf System-Profile `tavily-basic`, `tavily-refined`, `run-attachments`, `knowledge-base-default` und `academic-default` (in Stufe 8c ausgemustert, D-089; bleibt für ältere Snapshots in der Regel) laufen über ihren unten genannten typspezifischen Provider, nicht über den Tool-Pfad, auch bei älteren Snapshots, die noch einen `ToolName` tragen. Nur eigene Profile mit `ToolName` nutzen den Tool-gestützten Provider ([10-tool-system](10-tool-system_de.md#a-t9--grounding-neuaufbau-auf-zentrale-tools)).
- Ein Provider, der übersprungen wird oder scheitert (fehlender Schlüssel, unbekannter Typ, Fehler), wird zur Warnung am Lauf (`grounding.provider_skipped`) statt zu still leerem Kontext. Der Code `grounding.tool_failed` ist reserviert und wird derzeit nie geschrieben; auch Fehler von Tool-Quellen laufen über `grounding.provider_skipped`.
- **Keine Suche außerhalb bei Standardaufträgen (D-079):** Ein Lauf eines Auftrags mit der Standard-Crew („Extra careful“ im Web oder über MCP, `submit_request` ohne `crewTemplate`/`customCrew`) behält nur die Quellen, die in Atelier bleiben (`vector-store`, `order-sources`, `learning-retrieval`, `static-context` und die Kataloge, nie über den Tool-Pfad); Web-, Nachrichten-, Wissenschafts-, REST-, URL- und Tool-Quellen fallen vor dem Bau der Pipeline weg, auch wenn der Lauf mit einem gespeicherten Snapshot fortgesetzt wird, der sie noch nennt. Ein bewusst genanntes Template (Werkstatt, `submit_request` mit `crewTemplate`), eine eigene und eine zusammengestellte Crew behalten ihre Quellen.
- **Suchanfrage scheitert geschlossen (D-079):** Mit `ExtractQuery` (bei jedem System-Profil an) sucht der `tavily`-Provider nur mit der Anfrage, die der `GroundingQueryExtractor` gebildet hat; schlägt der Aufruf fehl oder kommt keine Anfrage zurück, wird nichts gesucht, und die Quelle wird zu `grounding.provider_skipped`. Der Auftragstext ist nie die Ersatzanfrage. Nur ein Profil mit `ExtractQuery=false` sowie die Typen `news-search`, `academic-search` und `rest-api` schicken den Auftragsteil des Briefings unverändert.
- Seit Stufe 8c (D-089) liest kein verbliebener Grounding-Typ einen Schlüssel oder Header aus der Umgebung (`authHeaderEnv`/`apiKeyEnv` gehörten zu `rest-api`/`academic-search`); der Grounding-Editor ist nur für Admins.

### Provider-Typen

| Typ | Implementierung | Beschreibung | Settings |
|---|---|---|---|
| `tavily` | `TavilyGroundingProvider` | Web-Suche via Tavily API (Basic oder Advanced). API-Key pro Profil. | `Tier` (basic/advanced), `MaxResults`, `IncludeAnswer` |
| `vector-store` | `VectorStoreGroundingProvider` | Semantische Suche in einer pgvector-Sammlung. Scope: `global` (nur gemeinsame Dokumente), `run-local` (Anhänge des Laufs und der Läufe, die er fortsetzt, nie die anderer Läufe) oder `both` (gemeinsame Dokumente plus diese Anhänge). Ein fehlender Scope bedeutet `global`; ein unbekannter Wert wird ebenfalls wie `global` behandelt (fail-closed, nie Anhänge anderer Läufe), und der Lauf bekommt die Warnung `grounding.scope_unknown` (D-067). `TagFilter` grenzt nur die gemeinsamen Dokumente ein; Anhänge haben keine Tags und werden davon nie ausgeblendet. | `TopK`, `Scope`, `TagFilter` |
| `static-context` | `StaticContextGroundingProvider` | Kuratierter Fixtext, der bei jedem Run unverändert injiziert wird. Keine externe API. Ideal für Style-Guides, Glossare, Markenstimme. | `label`, `content` (max 200.000 Zeichen, Soft-Limit 50.000) |
| `url-fetch` | `UrlFetchGroundingProvider` | Fetcht konkrete URLs, bereinigt HTML via HtmlAgilityPack, gibt Textinhalt zurück. SSRF-Guard blockiert private IPs. | `urls` (newline-separated), `maxContentPerUrl` (Default 8000), `stripBoilerplate` (bool, Default true) |
| `news-search`, `academic-search`, `rest-api` | — | In Stufe 8c ausgemustert (D-089). Gespeicherte Profile und ältere Snapshots bleiben lesbar; ein Lauf überspringt sie mit einer Warnung; speichern lassen sie sich nicht mehr. | — |

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

## Template Studio (D-043, entfernt)

Der KI-gestützte Vorlagen-Assistent unter `/crew/studio` wurde in Stufe 8c entfernt (D-089), zusammen mit seinem eigenständigen Start eines Kompositionslaufs. Auto-Crew bleibt über `/new` und `submit_auto_crew_request` erreichbar. Die MCP-Werkzeuge `analyze_template_proposal` und `materialize_template_proposal` bleiben als Stummel, die einen Fehler mit Verweis auf `submit_order` liefern. Die Tabellen `TemplateStudioAnalyses` und `StudioSettings` bleiben in der Datenbank. Die Transaktionsfabrik des Materialisierers (`IAtomicTransactionFactory`) liegt jetzt unter `Infrastructure/Persistence`.

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

Einstiegspunkte: das erweiterte Formular `/new` mit Auto-Crew und das MCP-Werkzeug `submit_auto_crew_request` (beide hängen einen Aufgabenlauf an). Der eigenständige Start über `/crew/studio` ist mit dem Template Studio in Stufe 8c entfallen (D-089).

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
4. **Materialize** — für Modi `composed` / `new`: legt Profile (Executor, Reviewer × N, Advisor × N, GroundingProvider × N, Finalizer × N) und das Crew-Template in einer einzigen EF-Core-Transaktion an (`IAtomicTransactionFactory`).
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

1. **Upload-Platz der Person und globaler Platz.** Jede Person hat höchstens `Sources:MaxConcurrentUploadsPerUser` (2)
   Uploads gleichzeitig; ein weiterer wird sofort mit HTTP 429, `Retry-After: 5` und „You are already adding 2 files at
   the same time. Please wait until one is done, then try again.“ abgelehnt. Über alle Personen laufen höchstens
   `Sources:MaxConcurrentIntakes` Uploads gleichzeitig; wer keinen Platz bekommt, wartet bis
   `Sources:ExtractionQueueTimeout`, sonst kommt HTTP 503 mit `Retry-After: 30` und der Quittung „The service is busy
   with other uploads. Please try again in a minute.“ (die Karte bietet „Try again“ an). Beide Plätze werden **vor** dem Lesen des Request-Bodys belegt: `POST /api/sources` liest den
   Multipart-Body als Strom (kein Formularpuffer) und nimmt das Antiforgery-Token nur aus dem Header. Eingefügter Text
   braucht keinen der Plätze, weil er schon im Speicher liegt.
2. **Streamen und Zeit.** Der Upload wird nach `<StorageRoot>/.incoming/<id>/upload.part` geschrieben (Verzeichnis
   0700, Datei 0600), unterwegs gehasht und bei `Sources:MaxFileBytes` begrenzt. Im Speicher bleiben nur die ersten
   8 KiB für die Typerkennung. Die Anfrage endet nach `Sources:UploadTimeout` (5 Minuten, HTTP 408 „The upload took too
   long. Check your connection and try again.“) und, hinter Kestrel, wenn sie langsamer als
   `Sources:MinUploadBytesPerSecond` sendet (4 KiB/s nach 10 s Schonfrist); beides gibt die Plätze frei. Ein kaputter
   oder abgebrochener Body endet als 400 „The upload could not be read.“; nur eine Datei über der Größengrenze bekommt
   413.
3. **Wiederverwendung und Kontingent.** Eine gleiche Datei desselben Besitzers wird wiederverwendet (der eindeutige
   Index `UX_Sources_OwnerUsername_Sha256` sorgt dafür, dass parallele Uploads derselben Datei bei einer Quelle
   enden). Dateien, die kein Auftrag nutzt, zählen gegen `Sources:MaxUnattachedSourcesPerUser` (50),
   `Sources:MaxUnattachedBytesPerUser` (500 MB) und `Sources:MaxUnattachedCharsPerUser` (20 Mio. Zeichen Text). Das
   Kontingent wird vor dem Lesen und noch einmal in der einfügenden Transaktion unter einer Sperre je Person
   (`pg_advisory_xact_lock(hashtext(owner))`) geprüft, sodass parallele Uploads es nicht gemeinsam überschreiten.
4. **Typprüfung.** Die Art ergibt sich aus dem Inhalt. Bevor der Webprozess ein ZIP-Archiv öffnet, liest er dessen
   Endsatz: mehr Einträge als `Sources:MaxArchiveEntries`, ein zentrales Verzeichnis über 1 KiB je erlaubtem Eintrag
   oder ein ZIP64-Verzeichnis (keine Word-Datei braucht eines) lehnen die Datei ab, ohne das Archiv aufzulisten.
   `%PDF-` in den ersten 1.024 Byte gilt absichtlich als PDF (Leseprogramme akzeptieren die Kopfzeile dort überall).
5. **Extraktion.** Text und Markdown liest der Webprozess selbst. PDF und Word belegen einen Extraktionsplatz
   (`Sources:ExtractionMaxConcurrency`, Standard 2) und laufen als
   `dotnet <Anwendungs-Assembly> --extract-source <pdf|docx> <datei>`. `Program.cs` verzweigt in diesen Modus, bevor ein
   Host gebaut wird: kein DI, keine Datenbank, keine Konfiguration, keine Anmeldung. Die Antwort des Prozesses wird
   gelesen, während sie hereinkommt; sie liegt nie als ganzes Byte-Array im Speicher.
6. **Textgrenze.** Eine Quelle hat höchstens `Sources:MaxExtractedChars` Zeichen (3 Mio.; ein Buch mit 1.000 Seiten
   hat rund 2 Mio.). Word-Dateien werden schon beim Prüfen ihrer XML-Teile gezählt, bevor das Dokumentmodell entsteht,
   und noch einmal beim Durchlaufen; das Lesen einer PDF endet bei der Seite, die die Grenze überschreitet; Textdateien
   werden vor dem Dekodieren gezählt; der Webprozess prüft die Länge noch einmal, bevor er den Text kopiert. Die
   Quittung lautet „This file contains far more text than we can use. Please upload a shorter version.“
7. **Aufbereitung in einem Durchlauf.** Zuerst werden die Zeilenenden zu `\n` (ein einzelnes `\r` beginnt also eine
   Zeile, bevor irgendetwas die Zeilen prüft), Zeilen, die wie `[[page N]]` aussehen, werden in Text-, Markdown- und
   Word-Dateien entschärft, lange Zeilen umbrochen (jedes Stück wird ebenfalls entschärft) und Zeichen und Wörter ohne
   Seitenmarken gezählt. Text, der nichts davon braucht, wird nicht kopiert. Echte Marken schreibt nur der
   PDF-Extraktor.
8. **Ablage.** Die empfangene Datei wird nach `<StorageRoot>/<Besitzer-Hash>/<id>.<endung>` verschoben; das
   temporäre Verzeichnis wird in jedem Fall entfernt.

### Grenzen des Extraktionsprozesses

| Grenze | Option | Standard | Wirkung |
|---|---|---|---|
| Gesamtzeit | `ExtractionTimeout` | 60 s | ganzer Prozessbaum wird beendet, „Reading this file took too long.“ |
| Verwalteter Speicher | `ExtractionMemoryLimitBytes` | 1 GiB | `DOTNET_GCHeapHardLimit` im Kind; ein Wächter beendet es zusätzlich oberhalb der Grenze plus 256 MiB |
| Text | `MaxExtractedChars` | 3.000.000 Zeichen | Lesen endet, „This file contains far more text than we can use. Please upload a shorter version.“ |
| Antwortgröße | `ExtractionMaxOutputBytes` | 20 MiB | Prozess wird beendet, Datei unlesbar; bemessen auf die Textgrenze mit höchstens sechs Byte je UTF-16-Einheit (Emoji und Formatzeichen werden als `\uXXXX` geschrieben, Buchstaben der Grundebene nicht maskiert) plus Platz für das JSON |
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
Wurzel und die Besitzerordner darunter, überspringt Links und löst jeden Pfad innerhalb der Wurzel auf. Sind
mindestens 10 abgelegte Dateien unbekannt und machen sie mehr als die Hälfte aller alten abgelegten Dateien aus, passt
die Datenbank vermutlich nicht zum Volume (aus einer älteren Sicherung wiederhergestellt oder leer gestartet); dann
löscht er keine abgelegte Datei und protokolliert einen Fehler.

### Hinweis für den Betrieb

Die Compose-Datei begrenzt den Speicher des Web-Containers auf 6 GiB (`mem_limit: 6g`), als zweite Schicht hinter den
Grenzen oben: Der Webprozess bleibt üblicherweise unter 1 GB, jeder Extraktionsprozess darf 1 GiB verwalteten Speicher
(plus den Spielraum des Wächters) nutzen, und höchstens zwei laufen gleichzeitig; der Rest ist Puffer. Das Limit
schützt die anderen Dienste eines gemeinsam genutzten Hosts. Hat der Host weniger Speicher übrig, senkt man
`ExtractionMaxConcurrency` oder `ExtractionMemoryLimitBytes` zusammen mit dem Limit.

## Automatische Längenprüfung des Direktwegs (Zusatz zu D-070)

Die Längenprüfung ohne Modell ist bewusst konservativ: Sie blockiert nur bei eindeutiger Lage und grober Abweichung;
sonst bleibt es bei einer Notiz. `RequestConstraintExtractor` liest Längen nur außerhalb von Anführungszeichen, Code
und Text, der nach einer Einleitung angehängt ist („Antworte auf diese Mail:“). `LengthRuleGate` lässt eine Länge nur
dann zur blockierenden Prüfung werden, wenn der ganze Auftrag genau eine Längenangabe in Wörtern oder Zeichen enthält
(oder ein klares Paar wie „zwischen 100 und 150 Wörter“), keine andere Mengenangabe (Stückzahlen, „je/pro/each“,
Varianten, Seiten, Minuten), kein Wort mit Rückbezug oder Überarbeitung („Entwurf“, „letzte“, „zu lang“, „kürze“,
„zusammenfassen“, „übersetze“ …), ein an die Zahl gebundenes Grenzwort („höchstens“, „at most“, „ca.“ … oder „mit/von/in
N Wörtern“ direkt hinter dem Ergebnis), keine fremde Präposition zwischen Befehlsverb und Länge im selben Teilsatz
(„einen Titel für ein Buch mit mindestens 80.000 Wörtern“) und keine Zusammensetzung wie „300-Wörter-Text“. Jede
andere Länge ergibt die Notiz „Your request mentions a length (…); it was not checked automatically.“ Auch eine
blockierende Länge löst eine Korrektur nur aus, wenn der Text die Grenze um mehr als 25 % verfehlt („höchstens 100
Wörter“ ab 126 Wörtern, „mindestens 200 Wörter“ unter 150); eine kleinere Abweichung ist ein offener Punkt. Ist die
Länge der einzige blockierende Befund, endet die Anweisung an den Schreiber mit „Keep everything else unchanged.“ Die
Falsch-Blockier-Rate wird an synthetischen, unabhängig beschrifteten Aufträgen gemessen
(`tests/Geef.Atelier.Tests/Domain/Checks/Corpus/`, `LengthCorpusTests`).

## Vorlage `direct`: zwei Prüfer (D-073)

Die Vorlage `direct` existiert nur im Code (`SystemCrew.DirectTemplate`, in keiner Vorlagenliste). Der Schreiber schreibt
einmal und korrigiert höchstens einmal (`MaxIterations: 2`). Seit Etappe 3 hat sie zwei Prüfer, beide setzt
`DirectPipelineAssembler` beim Bau der Pipeline ein:

| Prüfer | Profil | Modell | Was er tut |
|---|---|---|---|
| Automatische Prüfungen | `deterministic-checks` | keins | `DraftCheckEvaluator`: nicht leer, keine Vorrede, Länge, Sprache, Platzhalter (in eckigen Klammern, bis 300 Zeichen auf einer Zeile; der Faktenprüfer verwirft eine gemeldete Stelle innerhalb eines Platzhalters nur bis 200 Zeichen, damit eine Aussage in einer längeren Klammer stehen bleibt), verbotene Formen und Wörter, verlangte Wörter, ausgeschriebene Rechnungen. Blockierendes → `Error`, Rest `Info`. |
| Faktenprüfung | `source-fact-check` | Slot `FactReviewer` (heute Codex Astra, nie die Familie des Schreibers) | `SourceFactCheckReviewer`: vergleicht die Tatsachen im Text mit Wortlaut, Antworten, Profil und Dateien (`FactCheckPrompts`, Version `fact-check-1`). |

Die Faktenprüfung läuft bei jedem Auftrag des Direktwegs. Nur `Direct:FactCheckMode = Off`, `Shadow` oder ein Ausfall des
Modells samt seinem einen Ersatz lassen die Tatsachen ungeprüft, und „What was checked“ sagt das.

- **Material** kommt aus dem Grounding (`AtelierContextKeys.FactMaterial`): Wortlaut, Antworten der Person und des Clients,
  Profil und Dateien, bei einer Überarbeitung der Ausgangstext als `S0`. Annahmen und Notizen des Schreibers sind nie
  Material. Über `Direct:FactCheckMaxMaterialChars` (150.000 Zeichen Material und Text) bekommt die Prüfung die Dateikarte
  statt des Inhalts.
- **Filter des Codes** (`FactFindingFilter`): Ein Befund bleibt nur mit wörtlichem Zitat; ob er blockiert, folgt aus der
  Fundstelle (Wortlaut, Antworten der Person und Profil blockieren; Antworten des Clients sind Hinweise; Widersprüche der
  Dateien blockieren nie). Eine Begründung mit Link, Adresse, Konto- oder Zugangsdaten verwirft den Befund nie: Die Begründung
  wird durch einen festen Satz ersetzt, das Zitat aus dem Text bleibt (eine erfundene IBAN muss die Person sehen); unsichere
  Materialstellen werden ebenso ersetzt. „Wörtlich gedeckt“ verlangt ganze Wörter.
- **Jede Fassung wird voll geprüft.** Ein aus beliebigem Grund geänderter Text (Faktenbefund, Länge, Sprache …) bekommt
  erneut ein volles `check_facts` mit denselben Ersatzregeln; nur eine Fassung, die eine frühere Runde schon wortgleich
  geprüft hat, übernimmt deren Befunde ohne Aufruf. `recheck_facts` wird nicht mehr aufgerufen (Prompt und Schema bleiben im
  Code unverändert). Ein Modell, das in seine Zeitgrenze lief, wird im selben Lauf nicht erneut gerufen. Der Bericht kennt die
  geprüfte Fassung (`FactCheckRound.TextHash`); ein ausgelieferter Text, den keine Prüfung sah, ist `VersionNotChecked` („The
  facts of this version of the text were not checked …“), und frühere Befunde erscheinen nur, wenn ihre Stelle noch darin steht.
- **Kein Befund geht zwischen Runden verloren** (`FactCarryOver`): Nach jeder vollen Prüfung bleiben die Befunde aller früher
  beantworteten Runden offen, deren Stelle noch wörtlich (ganze Wörter) im Text steht, mit ihrer Blockierwirkung, und jeder
  Quellenwiderspruch bleibt (eine Korrektur ändert die Dateien nicht). Entdoppelt werden nur Wiederholungen: eine Stelle, die
  ganz in einer behaltenen Stelle gleicher Art liegt (bei Vorgaben gegen dieselbe Vorgabe), bzw. ein Widerspruch, dessen beide
  Materialstellen einander an Wortgrenzen enthalten und der dieselben Dateien nennt; eine nur überlappende Stelle wird
  zusätzlich gezeigt. Keine Kappe, IDs neu. Wird eine frühere Fassung als die beste ausgeliefert, kommen die Befunde späterer
  beantworteter Runden auf dieselbe Weise dazu (Widersprüche immer, Stellen nur, wenn sie im ausgelieferten Text stehen, nicht
  gelistete mitgezählt). In `On` lehnt eine
  blockierende Stelle, die die zweite Prüfung nicht erneut meldet, deshalb weiter ab. Nicht gelistete Stellen früherer Prüfungen
  bleiben gezählt. Zitate werden auf ganze Wörter erweitert, damit eine unveränderte Stelle nie als „entfernt“ gilt; ohne
  Prüfung der ausgelieferten Fassung ergänzt „Removed or replaced“, dass die Aussage umformuliert noch enthalten sein kann.
- **Stufen:** `Report` (ausgeliefert) → Befunde `Warning`, Urteil `Approved`, der Text wird nie geändert; `On` → blockierende
  Befunde `Error` und `Rejected`, der Schreiber bekommt feste Sätze; `Shadow` → `Info` mit Metadatum `shadow`, nichts sichtbar.
- **Ersatzmodell:** Ist das Modell nicht erreichbar, folgt ein Versuch mit dem nächsten Kandidaten von `FactReviewer`, der
  weder das Schreibermodell noch vom gescheiterten Anbieter noch bezahlt ist (`FactCheckModelFallback`, heute Sonnet). Das
  Failover des Gateways bleibt aus. Aufrufe, Modell und Ersatz stehen im Bericht (`factCheck`), nie als Warnung.
- **Persistenz:** Befunde behalten `Category = fact-check`, `Code` (`fact.unsupported`, `fact.instruction`, `fact.superseded`,
  `fact.source_conflict`) und `Quote`; `Runs.FactCheckMode` hält den Modus des Laufs (Migration Step50). Wird ein Konto gelöscht
  oder umbenannt, verlieren seine Läufe jede Kopie der About-me-Angaben: den Block im Auftrag, den Auftrag in den gespeicherten
  Ereignissen (`PipelineStartedEvent.Input` und jeder andere Text eines Ereignisses, der den Block oder die Angaben wörtlich
  enthält) und was die Faktenprüfung in Bericht, Befunde und Ereignisse kopiert hat. Läufe, die eine frühere Version schon
  bereinigt hat, werden mit den Angaben aus ihren Ereignissen erneut bereinigt.
- **Die Rechenprüfung sagt nie „stimmt“:** Die Prüfung ausgeschriebener Rechnungen im Text und der Summen in den Dateien nennt
  nur, was eindeutig falsch aussieht, und bittet die Person, es zu prüfen; sonst sagt „What was checked“ höchstens, dass nichts
  eindeutig Falsches gefunden wurde und dass nicht jede Rechnung so prüfbar ist. Frühere Berichte lesen sich ebenso.

## Recherche im Grounding-Schritt (D-074)

Seit Etappe 4 kann der Grounding-Schritt des Direktwegs (`OrderGroundingStep`) Ausschnitte aus dem Web anhängen. Er fragt
`IOrderResearchService.EnsureAsync`, bevor er die Dateien rendert, mit der Version des Laufs (vom Assembler), ob der Lauf einen
Text überarbeitet, ob der Auftrag angehängte Dateien hat, und den Angaben „Über mich“ aus dem Briefing. Ein Auftrag mit
irgendeiner Datei (gleich welche Rolle) sucht nie: Der Dienst speichert einmal den Status `FilesAttached`, und nichts erreicht
Suche oder Verdichtung. Sonst hält die Herkunftsregel (`ResearchQueryGate`) ein Suchthema mit einem Wort aus den Werten von
„Über mich“ (ohne Feldnamen), das nicht im Wortlaut steht, zurück. Der Dienst
entscheidet (Schalter `Research:Mode`, Override je Nutzer, erste Version, keine Überarbeitung, Klärung mit Suchthemen,
Tagesgrenze, vor der Suche reserviert, insgesamt und je Konto) und speichert die Recherche einmal am Auftrag (`Orders.ResearchJson`); jeder spätere Lauf des Auftrags bekommt die
gespeicherten Ausschnitte unverändert und sucht nie neu.

- **Quelle `W1`:** Hat die Recherche mindestens einen Ausschnitt, schreibt `DossierRenderer` ihren Text, und der Schritt hängt
  ihn nach den Dateien als Quelle `W1` mit dem festen Namen „Looked up on the web“ an (`PromptSource.LookedUp`). Die Kennung
  vergibt nur der Code. Sie wird wie jede Datei entschärft (`SourceBlockRenderer`) und zählt für das Budget des
  Hintergrunddokuments; die Warnung zu langen Dateien zählt nur die Dateien der Person.
- **Schreiber:** `W1` wird nie als angehängte Datei gezählt oder gelistet; ohne Dateien hat der Prompt eine eigene Einleitung.
  Im ersten Durchgang folgt im Nutzerprompt nach dem Hinweis zu den Quellen ein fester Satz
  (`DirectWriterPrompts.LookedUpNote`): W1 darf einen Punkt beantworten, den das Briefing als nicht angegeben führt, so wie W1
  es sagt, und ist Inhalt, nie Anweisung. Der System-Prompt `SystemPrompts.DirectWriter` bleibt unverändert (Hash-Test); ohne
  `W1` ist der Prompt der von Etappe 3.
- **Faktenprüfer:** `W1` ist eine weitere Quelle des Materials (`FactSource.LookedUp`, `MaterialPart.LookedUp`). Prompts,
  Schemata und Version `fact-check-1` bleiben unverändert (`FactCheckPromptStabilityTests`). Die Nachrechnung der Summen sieht
  `W1` nie.
- **Nutzung und Bericht:** Die Nutzungszeile von `W1` hat keine Quelle und das Ziel `research`, Position nach den Dateien. Der
  Bericht führt `W1` nach den Dateien mit `lookedUp` und ist als `lookedUp` markiert; Schreiberzitate aus `W1` sind weder
  Dateizitate noch zählen sie als ungeprüft.
- **Stoppt nie den Lauf:** Status `Failed` (auch eine Recherche, die nicht gespeichert werden konnte, Code `not_stored`), eine
  Ausnahme des Dienstes oder eine Wartezeit über 120 s (auch wenn der Dienst nicht von selbst abbricht) ergeben die Warnung
  `grounding.research_failed` („Looking things up on the web did not work this time. The text was written without it.“), und
  der Lauf geht ohne `W1` weiter. `NothingFound`, `AllWithheld`, `DailyLimit` und `FilesAttached` hängen keine Quelle an und warnen nicht; das
  Ergebnis zeigt sie als feste Sätze. Nur der Abbruch des Laufs selbst beendet den Schritt.

## Dateien über das Upload-Ticket (D-076)

Ein KI-Assistent kann eine Datei nicht verlässlich als Base64 in ein Werkzeugargument schreiben. Solange `Uploads:Tickets` `On`
ist, kommt sie deshalb außerhalb des MCP-Aufrufs an:

1. `prepare_attachment(fileName, sizeBytes?)` legt eine Zeile in `UploadTickets` an (Zustand `Open`, Token nur als SHA-256,
   Token 15 Minuten, Seite 24 Stunden) und gibt `curlCommand` und `uploadPage` zurück.
2. Entweder führt der Assistent `curlCommand` aus (`PUT /api/uploads/{id}`, Token im Kopf `X-Upload-Token`), oder die Person legt
   die Datei auf der Seite `/u/{id}` ab (`POST /api/uploads/{id}/page`, angemeldet). Beide Wege beanspruchen das Ticket in einer
   Anweisung (`Open → Receiving`, `Attempts + 1`, `ReceivingSince = jetzt`) und geben die Datei an dieselbe Aufnahme wie die
   Weboberfläche (`ISourceIntakeService.IntakeAsync`: Erkennung am Inhalt, Extraktion im Kindprozess, Kontingente). Herkunft der
   Quelle: Token-Weg `McpAttachment`, Seite `WebUpload`.
3. Brauchbar → `Used` mit `SourceId`. Geprüft und abgelehnt → zurück auf `Open`, der Versuch bleibt gezählt, der Code steht in
   `LastRefusalCode`; nach 3 solchen Versuchen `Failed`. Nicht geprüft (zu viele gleichzeitige Uploads, Überlast, Abbruch,
   Speicherfehler, Extraktion nicht verfügbar, volles Kontingent, Zeitgrenze, Serverfehler) → zurück auf `Open`, der Versuch wird
   zurückgegeben (`Attempts - 1`). Eine Datei, die zu lang ist, um ganz verwendet zu werden, wird auf diesem Weg abgelehnt
   (`too_long_for_ticket`; Seiten wählen geht nur in der Web-App) und nicht gespeichert. Abschluss und Freigabe gelten nur für den
   eigenen Anspruch (gleiches `ReceivingSince`).
4. `submit_order(…, uploadIds)` verknüpft die Quellen der angekommenen Tickets der angemeldeten Person mit dem Auftrag (alles oder
   nichts); ab hier ist die Datei eine normale Quelle des Auftrags.

**Aufräumen** (stündlicher Aufräumdienst der Quellen): `Receiving` länger als `Sources:UploadTimeout` + 1 Minute → `Open` bzw.
`Failed` (nach einem Absturz; ein laufender Upload endet vorher an der Zeitgrenze seiner Anfrage). Dieselbe Frist prüft der
Ticketdienst bei jedem Zugriff auf das Ticket (Seite, Token- oder Seiten-Upload) und gibt ein hängendes Ticket sofort frei, statt
auf den nächsten Lauf zu warten; der Versuch bleibt dabei gezählt. Tickets 7 Tage
(`Uploads:TicketRetention`) nach Ablauf der Seite gelöscht; Zeilen in `SourceRefusals` nach 180 Tagen
(`Uploads:RefusalRetention`). Wird eine Quelle gelöscht, wird `UploadTickets.SourceId` `NULL`.

**Ablehnungen zählen.** `SourceRefusals` hält je abgelehnter Datei Zeit, Person, Weg (`web`, `mcp_inline`, `mcp_ticket`,
`upload_page`), Code, erkannte Art, Endung, angegebenen Typ und Größe, nie Name oder Inhalt. Nicht gezählt werden ein
Token-Upload über der Anfragegrenze (`413` vor dem Anspruch), ein ungültiger Token, eine Datei, die die Upload-Seite schon im
Browser abweist, ein Upload, der an der Zeitgrenze endet oder abgebrochen wird, und technische `400`; `too_large` ist damit
untererfasst. Grundlage der Entscheidung über
weitere Dateiarten (Auslöser in D-076: mindestens 3 Ablehnungen von mindestens 2 Konten außerhalb der Messkonten in 60 Tagen).
Auswertung, nur lesend; die Liste der Messkonten kommt aus der Betriebskonfiguration (`-v messkonten='{konto1,konto2}'`):

```sql
SELECT "Code", coalesce("Extension", '-') AS "Endung", "Path" AS "Weg",
       count(*) AS "Ablehnungen", count(DISTINCT "OwnerUsername") AS "Konten"
FROM "SourceRefusals"
WHERE "CreatedAt" > now() - interval '60 days'
  AND NOT ("OwnerUsername" = ANY (:'messkonten'::text[]))
GROUP BY 1, 2, 3
ORDER BY 4 DESC, 1, 2;
```

## Nachbesserungslauf im Direktweg (D-075, Teil B)

Ein Lauf, der eine frühere Version des Auftrags ändert („Change something“, `revise_order`), läuft immer über den Direktweg. Er
trägt `RunEntity.RevisionHint` (die Änderung der Person samt Sätzen der Schnellknöpfe), `ParentRunId` (Lauf der gezeigten Version)
und `SeedDraftText` (deren ausgelieferter Text). Den Lauf legt der Auftragsdienst an; „Resume“ auf einem gescheiterten oder
gestoppten Nachbesserungslauf legt ihn genauso an (`RunService.ResumeRunAsync`: dieselbe `RevisionHint`, die jetzt gezeigte Version
als `ParentRunId` und Ausgangstext, ihr Brief neu zusammengesetzt, gespeichert über `TryAddRevisionRunAsync`). Er lehnt mit
`RunNotResumableException` ab und legt keinen Lauf an, wenn der Nachbesserungslauf schon einen eigenen Text hat
(`RevisionLines.ResumeChangedText`; die Laufseite zeigt den Satz statt des Knopfs) oder eine spätere Version des Auftrags einen Text hat
(`RevisionLines.ResumeLaterVersion`). Der Direktweg macht daraus Folgendes:

- **Grounding:** Der Assembler reicht `RevisionHint` an `OrderGroundingStep`; der Schritt setzt den Kontextschlüssel
  `AtelierContextKeys.RevisionHint`. Der frühere Text ist nur Ausgangstext, nie Material des Faktenprüfers. Die Recherche liefert wie
  bei jeder Überarbeitung nur Gespeichertes.
- **Brief:** `RevisionBrief.Compose` fügt die Änderung als Zeile „the change to make now“ an und markiert jede ältere Zeile des Blocks
  als „earlier change: already made in the text – do not make it again, keep it“; die Überschrift sagt, dass die neueste Änderung
  gilt und die Worte der Person den Blöcken „Assumptions …“ und „Not given by the person“ vorgehen. `BriefingSections.Parse` gibt die
  Änderungen getrennt von den Antworten zurück (`BriefParts.Changes`, die letzte als neueste), und das Material des Faktenprüfers
  hält sie so (`FactMaterial.Changes`). Nur der Prompt des Prüfers schreibt sie unter die Antworten der Person, mit kurzer Marke
  („newest change, version n“ / „earlier change, version n; a newer change wins where they differ“, `FactChange.Marked`) vor dem
  unveränderten Wortlaut. Nachgeschlagen werden Zitate nur in den Worten; in einem Nachbesserungslauf (`FactMaterial.IsRevision`)
  nimmt der Filter die Marke vorher aus jedem Zitat des Prüfers (`FactChange.WithoutMarks`), sodass ein reines Marken-Zitat keine
  Vorgabe ist und kein gespeichertes Zitat die Marke trägt. Im ersten Lauf eines Auftrags lässt der Filter die Zitate, wie sie sind.
- **Schreiber:** Nur im Nutzerprompt des ersten Durchgangs ersetzt `DirectWriterPrompts.RevisionInstruction` die `SeedInstruction`
  (für Anbieter ohne Dokumentmodus über dieselbe Ersetzung „the text in draft.md“ → „this text“). Wortlaut seit Tor N′ (D-075):
  Anschlusskorrekturen so klein wie möglich, Ton-, Stil- und Längenänderungen ohne neue Aussage und ohne anderes Verhältnis zwischen
  Absender und Angesprochenen, ein längerer Text führt aus, was er schon sagt. Der System-Prompt bleibt bitgleich (Hash-Test);
  Korrekturdurchgang unverändert.
- **Prüfungen:** Jede Regel des ersten Auftrags (`DraftCheckResult.IsFirstRequestRule`: Länge, Sprache, keine Platzhalter, verbotene
  Formen, verbotene und verlangte Wörter) ist im Nachbesserungslauf nur eine Notiz, nie blockierend (`DeterministicChecksReviewer.Evaluate`,
  Satz `FirstRequestNoteFormat`); leerer Text, Vorrede, Anzeige der Platzhalter, Rechenprüfung und Faktenprüfung laufen wie immer. Der
  Bericht trägt `RunReport.Revision`, und „What was checked“ nennt eingehaltene Regeln als Regeln des ersten Auftrags.
- **Vorgaben im Faktenprüfer:** Im Nachbesserungslauf (`FactMaterial.IsRevision`) blockiert ein Befund der Art `Instruction` nur,
  wenn sein Vorgabenzitat in der neuesten Änderung steht (auch wenn es zugleich anderswo steht). Jeder andere gefundene Ursprung
  (Auftrag, Antworten, „About me“, Assistent, frühere Änderung) ergibt einen Hinweis mit `FactFinding.BeforeLastChange`: nie
  blockierend, also im Modus `On` `Info` und nie im Korrekturauftrag. Übernommene Vorgaben-Befunde einer Vorversion werden ebenso
  bewertet (`FactFindingFilter.ForRevision` in `DirectReportBuilder.EarlierFindings`), und `FactCarryOver.Merge` macht einen solchen
  Hinweis nie wieder blockierend. Andere Befundarten und der erste Lauf bleiben unverändert.
- **Nie verlieren über Versionen (`DirectReportBuilder`):** Nach dem Lauf liest der Assembler den Bericht des `ParentRunId`. Dessen
  offene Befunde (Stellen und Quellenwidersprüche) gehen über `FactCarryOver.Merge` in den neuen Bericht: Stellen nur, wenn ihr Zitat
  als ganze Wörter noch dasteht, Widersprüche immer, jeweils mit `FactFinding.FromVersion` (ein schon übernommener Befund behält die
  Version, die ihn fand); meldet der neue Prüfer dieselbe Stelle, steht sie einmal als eigener Befund – außer der neue Prüfer ist `Off`
  oder im Schattenmodus, dann geht der übernommene vor. Eigene Befunde eines früheren Prüfers in `Off` oder Schattenmodus werden nicht
  übernommen, von ihm schon übernommene (mit `FromVersion`) immer. Offene Punkte und Annahmen des Schreibers der Vorversion, die die neue
  nicht wortgleich in derselben Art (`SameAssumptionKey`) wiederholt, stehen in `RunReport.EarlierOpenPoints` mit Version und
  `EarlierPointKind` (auch die von der Vorversion schon übernommenen), dazu ein fester Satz (`EarlierPointKind.Note`) für ihre Notizen und
  Stellen, die nicht gezeigt werden konnten. Hat die Vorversion keinen Bericht (z. B. „Extra careful“), sagt der feste Satz
  `ChecksNotTakenOverFormat`, dass ihre Prüfungen nicht übernommen und nicht erneut geprüft wurden. Kann der frühere Bericht nicht gelesen
  werden, steht im Bericht ein Satz dazu; der Lauf geht weiter. Wurde eine Vorversion ohne lesbaren Bericht selbst als „Ready – please
  check“ gezeigt (`DirectRevision.NeedsCheck`, bestimmt mit `OrderStateResolver`), setzt der Bericht `RunReport.EarlierNeedsCheck`; eine
  spätere Version erbt es aus dem Bericht ihrer Basis. `OrderStateResolver.ToRunState` liest es in jedem Prüfmodus als übernommenen
  blockierenden Punkt. Berichte anderer
  Läufe bleiben bitgleich (`null` wird nicht geschrieben).
