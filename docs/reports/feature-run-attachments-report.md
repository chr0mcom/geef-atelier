# Abschlussbericht: Run-Attachments — Direkter Document-Upload beim Briefing

*Datum: 2026-05-14 | PR #7 | Merge-SHA: f6832f9*

---

## Was umgesetzt wurde

### Domain-Erweiterung (D-036 ohne Refactor erweitert)

- **`KnowledgeScope`-Enum** (`Global=0`, `RunLocal=1`) — typsicher, default 0 = Global für Backward-Compat bestehender Docs
- **`KnowledgeDocument`** um `Scope` und `Guid? RunId` erweitert (letzte zwei positionale Parameter)
- **`SystemCrew.RunAttachmentsProfile`** — `ProviderType="vector-store"`, `ProviderSettings={"TopK":"5","Scope":"run-local"}`, `IsSystem=true`, `MaxQueriesPerRun=1`
- **`KnowledgeBaseDefaultProfile`** ProviderSettings um `["Scope"] = "global"` ergänzt (explizit statt implizit)

### Persistence Layer

- **Migration `Step15RunAttachments`**: `Scope integer NOT NULL DEFAULT 0`, `RunId uuid NULL`, FK auf `Runs.Id` (ON DELETE CASCADE), zwei Indizes, System-Profil `run-attachments` in `GroundingProviderProfiles`
- **`IKnowledgeDocumentRepository`** + Impl: `ListByRunAsync(runId, ct)`
- **`IVectorSearchRepository.SearchAsync`**: `KnowledgeScope? scopeFilter` + `Guid? runIdFilter` Parameter
- **`VectorSearchRepository`**: Raw-SQL WHERE-Klausel um Scope + RunId erweitert (typed `NpgsqlParameter` mit explizitem `NpgsqlDbType`)
- **`IRunPersistenceService`**: `UpdateSnapshotAsync` + `MarkRunFailedAsync` neu

### Application/Infrastructure Layer

- **`SubmitRunRequest`-Record** — `IRunService` von positional Args auf Record umgestellt (cleaner, langfristig wartbar). Alle Aufrufer (Web, MCP, Tests) auf neue Signatur aktualisiert
- **`RunAttachmentInput`-Record** — `(Filename, ContentType, byte[])` — `byte[]` statt `Stream` um Lifetime-Probleme über async Boundaries zu vermeiden
- **`IKnowledgeService`** + Impl: `UploadRunAttachmentAsync`, `ListRunAttachmentsAsync`, `PromoteToGlobalAsync`; `ListAsync` um `KnowledgeScope? scope` Parameter erweitert
- **`RunService`**: Pre-Persist-Attachment-Upload (Run zuerst → FK gültig → Attachments uploaden → Snapshot mit RunAttachmentsProfile prepended → `UpdateSnapshotAsync` → Pipeline freigeben); try/catch → `MarkRunFailedAsync` bei Fehler; kein orphaned Pending-Run möglich
- **`VectorStoreGroundingProvider`**: Liest `Scope`-Setting aus `ProviderSettings`, mappt auf `scopeFilter`/`runIdFilter` für `SearchAsync`

### UI Layer

- **`FileDropZone.razor`**: Multi-File, Click-to-Browse (Drag-and-Drop entfernt — Blazor Server serialisiert `event.dataTransfer.files` nicht über SignalR), Typ/Größen/Count-Validierung, Remove-Button, zwei-Wege-Bindung
- **`New.razor`**: FileDropZone zwischen Briefing und Crew-Selector; Submit liest `IBrowserFile` via `OpenReadStream` in `byte[]`; Loading-State "Processing N attachment(s) and starting run..."
- **`RunDetail.razor`**: `RunAttachmentsList` + `PromoteAttachmentModal` integriert; `_runAttachmentDocumentIds` als Field (keine LINQ-Alloc pro Render)
- **`RunAttachmentsList.razor`**: Aufklappbare Liste mit Filename, Größe, ChunkCount, Promote-Button
- **`PromoteAttachmentModal.razor`**: Modal mit Title-Override, Description, Tags (TagInput), Confirm ruft `PromoteToGlobalAsync`
- **`GroundingSection.razor`**: Citations mit "From this run's attachments" vs. "From your knowledge base" Badge (Parameter `RunAttachmentDocumentIds`)
- **`KnowledgeIndex.razor` + `ListKnowledgeDocumentsTool.cs`**: Passieren jetzt `KnowledgeScope.Global` an `ListAsync` — Run-lokale Attachments bluten nicht in globale KB-Ansicht durch

### MCP-Tool

- **`submit_request`**: Optionaler `attachments`-Parameter — JSON-Array `[{"filename":"...","contentType":"text/plain","contentBase64":"..."}]`; `[property: JsonRequired]` auf allen Feldern; nur `text/markdown` und `text/plain` erlaubt (kein `application/octet-stream`)

---

## Architect-Output zu den 3 Knackpunkten

1. **Schema-Strategie: Erweiterung `KnowledgeDocuments` vs. separate Tabelle** — ✅ Erweiterung. Eine Such-Logik, weniger Code, FK direkt auf Runs, Scope-Filter transparent für VectorSearchRepository.

2. **Run-Persist + Attachment-Upload Sequenz: Option (a)** — ✅ Akzeptiert. Run mit `Status=Pending` persistieren → Attachments uploaden (FK jetzt gültig) → Snapshot patchen via `UpdateSnapshotAsync` → Pipeline freigeben via `QueueRunAsync`. Failure-Modus: `MarkRunFailedAsync` markiert Run als fehlgeschlagen, kein Orphan. `IRunPersistenceService` um beide Methoden erweitert, `RunPersistenceService` implementiert.

3. **Multi-Provider-Vorrang bei Attachments + Custom-Template** — ✅ Akzeptiert. `RunAttachmentsProfile` wird via `Prepend` vor alle anderen Provider gehängt (specific > general). `MultiProviderGroundingStep` respektiert Snapshot-Reihenfolge — keine Änderung nötig.

---

## Foundation-Wiederverwendungs-Beleg (D-036 erweitert, nicht refaktoriert)

| Asset aus D-036 | Wie erweitert |
|---|---|
| `IGroundingProvider`-Vertrag | Unverändert — `VectorStoreGroundingProvider` implementiert bereits |
| `VectorStoreGroundingProvider` | Nur `Scope`-Logik ergänzt — 20 Zeilen, keine Änderung am Interface |
| `IVectorSearchRepository.SearchAsync` | Signatur um 2 nullable Parameter erweitert, bestehende Tests angepasst |
| `KnowledgeDocument` / `KnowledgeDocumentEntity` | 2 neue Felder (Scope, RunId), bestehende Mapping-Logik unberührt |
| `SystemCrew` | Neue Konstante `RunAttachmentsProfile`, `KnowledgeBaseDefaultProfile` explizit gemacht |
| `MultiProviderGroundingStep` | Keine Änderung — aggregiert automatisch |
| `GroundingProviderFactory` | Keine Änderung — discovert per DI |

---

## UX-Verbesserung

**Vorher (Template-zentriert):**
1. Dokument unter `/crew/knowledge` hochladen
2. Custom-Template mit `knowledge-base-default` konfigurieren
3. Template beim Briefing wählen
4. Briefing schreiben + Run starten

**Nachher (Ad-hoc, ChatGPT/Claude-Pattern):**
1. Briefing schreiben + MD/TXT anhängen
2. Run starten

Attachments sind run-lokal — kein Namespace-Konflikt mit der globalen Wissensbasis. Promote-Funktion erlaubt nachträgliche Übernahme wertvoller Attachments in die persistente KB.

---

## Production-Deploy-Protokoll

**Backup:** `backup/before-run-attachments-migration-20260514-165517.dump` (39K, 60 TOC-Entries, PG 16.13)

**Migration `Step15RunAttachments`:**
```
20260515120000_Step15RunAttachments   ← neu (letzte ausgeführte Migration)
20260514120000_Step14VectorStore
20260513170000_Step13GroundingProviders
```

**Schema-Verifikation:**
```
-- KnowledgeDocuments: Scope integer NOT NULL DEFAULT 0 ✅
-- KnowledgeDocuments: RunId uuid NULL ✅
-- FK_KnowledgeDocuments_Runs_RunId ON DELETE CASCADE ✅
-- IX_KnowledgeDocuments_RunId (partial, RunId IS NOT NULL) ✅
-- IX_KnowledgeDocuments_Scope ✅
-- GroundingProviderProfiles: run-attachments (TopK=5, Scope=run-local) ✅
```

**Container:** `geef-atelier-web` neu gebaut (`--no-cache`), Status `healthy`. Keine Errors in Startup-Logs.

**Health:** `curl https://geef.stefan-bechtel.de/new` → HTTP 200 ✅

---

## Akzeptanzkriterien (20 Stück)

| # | Kriterium | Status |
|---|---|---|
| 1 | `dotnet build` 0 Errors, 0 Warnings | ✅ |
| 2 | `dotnet test` — alle 494 Tests grün (1 E2E-Skip pre-existing) | ✅ |
| 3 | Migration `Step15RunAttachments` läuft sauber | ✅ |
| 4 | System-Profile `run-attachments` in `GroundingProviderProfiles` | ✅ |
| 5 | NewRun-Page mit FileDropZone — Multi-File, Validierung | ✅ |
| 6 | Submit mit Attachments: Docs run-lokal indexiert, Provider auto im Snapshot | ✅ |
| 7 | Klassik MIT Attachments funktioniert — orthogonal zur Template-Konfiguration | ✅ |
| 8 | Klassik OHNE Attachments unverändert — kein Grounding, kein Embedding-Call | ✅ |
| 9 | VectorStoreGroundingProvider mit `Scope: run-local` sucht nur Run-Docs | ✅ |
| 10 | VectorStoreGroundingProvider mit `Scope: global` unverändert | ✅ |
| 11 | RunDetail zeigt Attachments-Liste mit Filename, Size, ChunkCount | ✅ |
| 12 | GroundingSection unterscheidet Run-Attachment-Citations visuell | ✅ |
| 13 | Promote-Funktion: Scope → Global, RunId → null, Tags gemerged | ✅ |
| 14 | ON DELETE CASCADE: Run-Delete löscht Attachments inkl. Chunks | ✅ |
| 15 | MCP `submit_request` mit `attachments`-Parameter, Base64-Decode | ✅ |
| 16 | File-Limit: >5 MB und >5 Files abgelehnt (UI + Service) | ✅ |
| 17 | Real-Test mit Attachment-Briefing | ⏳ (manuell nach Login im Browser) |
| 18 | Decisions-Log-Eintrag D-037 | ✅ |
| 19 | Merge auf `main` (PR #7 gemerged, Branch gelöscht) | ✅ |
| 20 | Production-Deploy verifiziert | ✅ (Migration ✅, Web-Container healthy ✅, HTTP 200 ✅) |

---

## Technische Besonderheiten / Lessons Learned

- **Blazor Server + Drag-and-Drop**: `DragEventArgs` serialisiert keine `Files`-Referenz über SignalR — Drag-and-Drop ist eine Client-API, die nicht über den SignalR-Kanal läuft. Entfernt, UI auf "Click to browse" umgestellt.
- **`to_regclass` vs. `::regclass`**: Ersteres gibt NULL zurück für nicht-existente Tabellen, Letzteres wirft Exception. Wichtig für idempotente Migrations-Guards in PostgreSQL 16.
- **Tag-Deduplication**: `.Union()` (nicht Spread-Syntax) für korrekte Deduplizierung bei `PromoteToGlobalAsync`.
- **`ListAsync`-Scope-Filter**: Ohne expliziten Scope-Filter würden Run-lokale Attachments in der globalen KB-Ansicht und im MCP-Tool auftauchen — `KnowledgeScope.Global` als Default für alle bestehenden Caller.
- **`IKnowledgeService` non-optional**: Silent failure bei null → als non-optional DI-Dep registriert, `NoOpKnowledgeService` für Tests die keinen KnowledgeService brauchen.

---

## Merge-SHA + Deploy-Timestamp

- **PR:** #7 `feat: Run-Attachments — Direkter Document-Upload beim Briefing`
- **Merge-SHA:** `f6832f9`
- **Branch gelöscht:** `feat/run-attachments`
- **Deploy-Timestamp:** 2026-05-14 ~14:56 UTC
- **Backup:** `backup/before-run-attachments-migration-20260514-165517.dump`

---

## Empfehlungen für Folge-Steps

**PDF-Support:** Eigener Step — `PdfPig` oder `iText7` für Text-Extraktion, dann gleicher Chunking/Embedding-Pfad. Attachment-Upload-Validierung in `FileDropZone` und `KnowledgeService` um `application/pdf` erweitern.

**Background-Job für Attachment-Indexing:** Aktuell synchron im Web-Request. Bei großen Docs (>50 Chunks, >1 MB) → `IBackgroundTaskQueue` + `BackgroundService` analog zu `RunOrchestratorService`. Statusspalte "IndexingStatus" auf `KnowledgeDocument`.

**Auto-Cleanup alter Run-Attachments:** Aktuell nur via Run-Delete (ON DELETE CASCADE). Retention-Policy (z.B. 90 Tage) als Background-Job würde Storage begrenzen.

**Image-Attachments:** `openai/clip-vit-large-patch14` für Bild-Embeddings — separater Provider-Typ, eigener Folge-Step.

**Attachment-Vorschau in RunDetail:** Inline-Rendering des Attachment-Texts (erste 500 Zeichen) für schnelle Inhaltsprüfung ohne Promote-Workflow.
