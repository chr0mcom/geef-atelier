# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Aktueller Zustand

**Post-Skeleton Schritt 1 abgeschlossen (11. Mai 2026): Postgres-Backup.** Walking Skeleton komplett + automatischer Backup-Service aktiv. App produktiv unter `https://geef.stefan-bechtel.de/`. Stack: drei Container (web, postgres, postgres-backup). Backup: täglich 03:00 UTC via `prodrigestivill/postgres-backup-local:16`, Retention 7/4/6, Volume `geef-atelier-backups`. Restore-Skript: `scripts/restore-backup.sh`. 85/85 Tests grün. Nächste Post-Skeleton-Schritte: LiveUpdateFlowTests-Stabilisierung, Cost-Tracking, RAG, Multi-User, Off-Site-Backup, Monitoring.

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
