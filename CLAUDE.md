# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Aktueller Zustand

**Run-Attachments deployed (14. Mai 2026).** PS-1 ✅ PS-2 ✅ PS-3 ✅ PS-4 ✅ PS-5 ✅ PS-6 ✅ PS-7 ✅ + CLI-Split ✅ + Model-Catalog ✅ + Grounding-Viz ✅ + Vector-Store-RAG ✅ + Run-Attachments ✅. D-037: `KnowledgeScope`-Enum (Global/RunLocal), `KnowledgeDocument.RunId` + FK mit `ON DELETE CASCADE`, `SystemCrew.RunAttachmentsProfile` (ProviderType="vector-store", Scope="run-local"), `SubmitRunRequest`-Record, `FileDropZone`-Komponente, `RunAttachmentsList`, `PromoteAttachmentModal`, `GroundingSection` Citation-Badges. 494 C#-Tests grün, 43 Python-Tests grün. D-036: `VectorStoreGroundingProvider`, `OpenRouterEmbeddingProvider`, `RecursiveCharacterTextSplitter`, pgvector 0.8.2. Postgres-Image: `pgvector/pgvector:pg16`.

Crew-System (PS-5/6): `ReviewerProfile`/`ExecutorProfile` als Records, `SystemCrew` als Code-Konstanten, `CrewSnapshot` (JSONB) pro Run, alle vier EvaluationStrategies, `ILlmClientResolver.ForProfile`. Crew-UI: 10 neue Pages unter `/crew`, 7 neue UI-Komponenten. Advisor-Pässe (PS-7): `AdvisorTrigger`-Enum (BeforeFirstExecution, BeforeEveryExecution, OnConvergenceFailure), System-Advisors `briefing-clarifier` (Strategic/BeforeFirst, gemini-2.5-flash) + `devils-advocate` (DevilsAdvocate/BeforeEvery, gpt-4o-mini), `AdvisorAwareExecutor`-Decorator, Convergence-Failure-Retry via `TryConvergenceFailureRetryAsync` + `RunEntity.AdvisorRetryAttempted`-Cap.

App produktiv unter `https://geef.stefan-bechtel.de/`. Nächste Post-Skeleton-Schritte: PS-8 (Cookie-Auth-Erweiterung); OnConvergenceFailure-Multi-Retry als ausstehender Punkt (aktuell Single-Retry-Cap).

**Wichtig für Production-Deploy nach CLI-Provider-Split:** `docker compose build --no-cache cli-proxy web && docker compose up -d`. Migration `Step12CliProviderSplit` (Daten-only) läuft automatisch. Legacy-Endpoint `/v1/chat/completions` bleibt vorerst aktiv (Deprecation-Warning im Log).

## Verbindlicher Workflow

Jede nicht-triviale Implementierungs-Aufgabe folgt **strikt** [`/srv/docker/docs/geef-workflow.md`](../../docs/geef-workflow.md) (vier Phasen Grounding/Execution/Evaluation/Finalize, fünf Reviewer, Pflicht-Advisors, Hard Rules). Bei Konflikt zwischen einem Bau-Prompt und dem Workflow gilt der Workflow.

Bau-Prompts in [`prompts/`](prompts/) sind absichtlich knapp: sie verweisen auf den Workflow und ergänzen nur den schritt-spezifischen Scope.

## Dokumentenhierarchie (Pflicht-Lektüre vor Implementierung)

In dieser Reihenfolge lesen — die Reihenfolge spiegelt Verbindlichkeit und Aktualität wider:

1. [`01-vision-and-scope.md`](01-vision-and-scope.md) — was im Scope ist und was bewusst nicht.
2. [`02-architecture.md`](02-architecture.md) — **verbindliche** Solution-Struktur, DB-Schema, GEEF-Provider-Mapping. Architect-Konsultationen prüfen Konsistenz, definieren die Struktur nicht neu.
3. [`03-walking-skeleton-plan.md`](03-walking-skeleton-plan.md) — die zehn Schritte mit Akzeptanzkriterien.
4. [`04-mcp-integration.md`](04-mcp-integration.md) — Tool-Verträge des MCP-Servers.
5. [`05-decisions-log.md`](05-decisions-log.md) — *Warum* die Architektur so aussieht. Eine Entscheidung darf nur mit neuem Eintrag im Log umgekehrt werden.

Updates an einer dieser Dateien aktualisieren auch das `Letzte Aktualisierung`-Feld der Datei (Living-Document-Konvention, keine Versionsnummern).

## Architektur-Kernsatz

Zwei Frontends (Blazor Server UI + MCP-Server), beide rufen denselben `IRunService` (Application Layer) → `RunOrchestratorService` (BackgroundService) → Geef-SDK-Pipeline (Grounding/Execution/Evaluation/Finalize) → Postgres via EF Core. Ein Auftrag ist ein Auftrag, unabhängig vom Eintragsweg.

**Solution (geplant):** `Geef.Atelier.{Core,Infrastructure,Web,Mcp}` + `Geef.Atelier.Tests`. Core ist LLM- und persistenz-frei. Infrastructure kapselt Postgres, LLM-Clients und alle Geef-Provider-Implementierungen. Web hostet UI + BackgroundService + `IRunService`-Implementierung. Mcp ist im Skeleton als ASP.NET-Stub angelegt, wird in Schritt 9 aktiv.

**DB-Schema (Skeleton):** vier Tabellen — `Runs`, `Iterations`, `Findings`, `Events`. Erweiterungen (Sources, AdvisorConsultations, ReviewerProfiles, CrewTemplates) kommen mit den entsprechenden Features, nicht vorab.

## Stack

- **.NET 10** (TargetFramework `net10.0`), `Nullable=enabled`, `TreatWarningsAsErrors=true`
- **Blazor Server** (SignalR für Live-Status; bewusste Wahl, siehe D-003)
- **Postgres** über `Npgsql.EntityFrameworkCore.PostgreSQL`; in Production gegen die existierende Server-Postgres-Instanz, kein eigener DB-Container
- **Geef SDK** ([chr0mcom/geef](https://github.com/chr0mcom/geef)) — sealed records, required init, immutable contexts, `geef:`-Präfix bei ContextKeys
- **Tests:** xUnit + Testcontainers.PostgreSql

## Konventionen (über die Repo-Regeln hinaus)

- **Code, Kommentare, XML-Docs:** Englisch (Geef-SDK-Konvention)
- **Doku, Berichte, Commits:** Deutsch (Projekt-Konvention)
- **Reviewer-Modell:** außerhalb der Anthropic-Familie für echte Außenperspektive (z.B. gpt-5.5 via Codex, aber hier wichtig immer das neuste zu nutzen!). Reviewer dürfen Anthropic nutzen, aber dann mit *anderem Modell* als der Executor.
- **Persistente Berichte:** Abschlussberichte aus Bau-Schritten kommen nach `reports/` (überlebt das Phase-4.3-Cleanup, weil der Pfad nicht der `geef_*.md`-Naming-Konvention entspricht)

## Auth-Modell (Skeleton)

- **UI:** Cookie-Auth, ein User aus `ATELIER_USER` / `ATELIER_PASSWORD_HASH`
- **MCP:** Bearer-Token aus `ATELIER_MCP_TOKEN` (OAuth 2.0 nach Skeleton)

## Was bewusst nicht im Skeleton steckt

Quellen-Upload/RAG, Klassifikator, dynamische Crew-Composition, Multi-Provider-Adapter (OpenAI/OpenRouter), Advisor-Pattern in der Pipeline, echtes Crash-Resume, Cost-Caps, DOCX/PDF-Export, Crew-Templates als versionierte Daten. Vor Erweiterung: erst Skeleton fertig, dann via Decisions-Log entscheiden.

## Beziehung zum übergeordneten Repo

Dieses Verzeichnis lebt unter [`/srv/docker/websites/`](../) — die Infrastruktur-Konventionen aus [`/srv/CLAUDE.md`](../../../CLAUDE.md) und [`/srv/docker/docs/`](../../docs/) gelten zusätzlich (insbesondere [`docker-deployment.md`](../../docs/docker-deployment.md), [`contact-protection.md`](../../docs/contact-protection.md), und der bereits referenzierte [`geef-workflow.md`](../../docs/geef-workflow.md)).
