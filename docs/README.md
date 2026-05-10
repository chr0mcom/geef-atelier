# Geef.Atelier — Projektdokumentation

Dies ist die laufend gepflegte Doku zum Projekt **Geef.Atelier** — einer Text-Manufaktur, die auf dem [Geef SDK](https://github.com/chr0mcom/geef) aufbaut und dessen GEEF-Pipeline (Grounding / Execution / Evaluation / Finalize) produktiv nutzt, um hochwertige Texte beliebiger Sorte zu erzeugen.

Die Doku entsteht parallel zum Brainstorming und wird nach jeder Entscheidungsrunde aktualisiert. Wenn ein Thema noch nicht ausgearbeitet ist, steht das explizit im jeweiligen Dokument.

## Dokumentenstruktur

| Datei | Inhalt |
|---|---|
| [01-vision-and-scope.md](01-vision-and-scope.md) | Was ist das Projekt, was nicht, für wen, in welchem Umfeld |
| [02-architecture.md](02-architecture.md) | Schichten, Komponenten, Datenfluss, DB-Schema |
| [03-walking-skeleton-plan.md](03-walking-skeleton-plan.md) | Der 10-Schritte-Bauplan für das erste lauffähige Skelett |
| [04-mcp-integration.md](04-mcp-integration.md) | MCP-Server-Anforderungen, Tools, Auth, Transport |
| [05-decisions-log.md](05-decisions-log.md) | Chronologisches Protokoll aller Entscheidungen aus dem Brainstorming |
| [prompts/](prompts/) | Konkrete Claude-Code-Prompts für die einzelnen Bauschritte |
| [reports/](reports/) | Persistente Abschlussberichte aus Claude-Code-Runs (überleben Phase-4.3-Cleanup) |

## Verbindlicher Entwicklungs-Workflow

Unter srv/docker/docs liegen diverse relevante Informationen zum Entwicklungsprozess, z.B. **`geef_workflow.md`** — das ist die kanonische Anweisung an Claude Code, *wie* gebaut wird. Es definiert die vier Phasen (Grounding / Execution / Evaluation / Finalize), die fünf Reviewer und die Advisor-Konsultationen. Alle Bau-Prompts in `prompts/` verweisen darauf und ergänzen nur den schritt-spezifischen Scope. Auch die anderen Dateien in dem Ordner könnten durchaus relevant sein und müssen beachtet werden.

## Status

**Phase:** Brainstorming abgeschlossen, Walking Skeleton in Vorbereitung.
**Letzte Aktualisierung:** 10. Mai 2026
**Nächster Schritt:** Schritt 1 ausführen (Solution-Setup mit Postgres und EF Core) — siehe [prompts/step-01-solution-setup.md](prompts/step-01-solution-setup.md).

## Konventionen

- Dokumentation auf Deutsch (passend zum Brainstorming-Kontext)
- Code, Code-Kommentare und XML-Doc-Comments auf Englisch (passend zum Geef SDK)
- Entscheidungen werden im Decisions-Log dokumentiert, bevor sie in andere Dokumente einfließen
- Living Document: keine Versionsnummern, stattdessen "Letzte Aktualisierung" pro Datei