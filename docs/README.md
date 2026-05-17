# Geef.Atelier — Projektdokumentation

Dies ist die laufend gepflegte Doku zum Projekt **Geef.Atelier** — einer Text-Manufaktur, die auf dem [Geef SDK](https://github.com/chr0mcom/geef) aufbaut und dessen GEEF-Pipeline (Grounding / Execution / Evaluation / Finalize) produktiv nutzt, um hochwertige Texte beliebiger Sorte zu erzeugen.

Die Doku entsteht parallel zum Brainstorming und wird nach jeder Entscheidungsrunde aktualisiert. Wenn ein Thema noch nicht ausgearbeitet ist, steht das explizit im jeweiligen Dokument.

## Dokumentenstruktur

| Datei | Inhalt | Typ |
|---|---|---|
| [01-vision-and-scope.md](01-vision-and-scope.md) | Was ist das Projekt, was nicht, für wen, in welchem Umfeld | Living |
| [02-architecture.md](02-architecture.md) | Schichten, Komponenten, Datenfluss, DB-Schema | Living |
| [03-walking-skeleton-plan.md](03-walking-skeleton-plan.md) | Der ursprüngliche 10-Schritte-Bauplan (historisch; Skeleton abgeschlossen) | Historisch |
| [04-mcp-integration.md](04-mcp-integration.md) | MCP-Server: Tools, Auth (Bearer + OAuth 2.1), Transport | Living |
| [05-decisions-log.md](05-decisions-log.md) | Chronologisches, append-only Protokoll aller Architektur-Entscheidungen (D-001 ff.) | Historisch |
| [06-reviewer-calibration.md](06-reviewer-calibration.md) | Severity-Taxonomie und Convergence-Policy-Standard für Reviewer | Living |
| [07-design-system.md](07-design-system.md) | Theme-System, Typografie, Komponenten-Inventar (Stand Design-Translation PS-3) | Living |
| [08-crew-system.md](08-crew-system.md) | Crew-/Profil-/Template-System, System-Profile, Advisor-Pässe | Living |
| [09-endpoint-reference.md](09-endpoint-reference.md) | Referenz aller extern erreichbaren HTTP-Endpunkte (MCP, OAuth, Web/Account) | Living |

> **Interne Arbeitsverzeichnisse (nicht im öffentlichen Repository):**
> `docs/prompts/` (schritt-spezifische Bau-Prompts) und `docs/reports/` (persistente
> Abschlussberichte je Bau-Schritt) sind bewusst über `.gitignore` ausgeschlossen und
> existieren nur in der lokalen Arbeitskopie. Verweise auf `reports/…` bzw. `prompts/…`
> in den historischen Dokumenten dienen der Provenienz und sind auf GitHub nicht
> auflösbar — das ist beabsichtigt.

## Verbindlicher Entwicklungs-Workflow

Im übergeordneten Infrastruktur-Repository unter `/srv/docker/docs/` liegen die übergreifenden Prozess-Vorgaben, allen voran **`geef-workflow.md`** — die kanonische Anweisung an Claude Code, *wie* gebaut wird (außerhalb dieses Repositories, daher hier nicht verlinkt). Sie definiert die vier Phasen (Grounding / Execution / Evaluation / Finalize), die fünf Reviewer und die Advisor-Konsultationen. Alle Bau-Prompts in `docs/prompts/` verweisen darauf und ergänzen nur den schritt-spezifischen Scope. Die weiteren Dateien in jenem Ordner (z.B. `docker-deployment.md`, `contact-protection.md`) sind ebenfalls verbindlich und müssen beachtet werden.

## Status

- **Phase:** Walking Skeleton abgeschlossen; produktiv unter `https://geef.stefan-bechtel.de`. Zahlreiche Post-Skeleton-Erweiterungen ausgeliefert (Crew-System, Advisor-Pässe, Grounding/Vector-Store-RAG, Run-Attachments, Cost-Tracking, Template Studio, Domain-Templates, MCP-OAuth 2.1, Multi-User, Run-User-Isolation).
- **Letzte Aktualisierung:** 17. Mai 2026
- **Aktueller Stand & nächste Schritte:** siehe [Projekt-README](../README.md) (Implementierungsstand) und [Decisions-Log](05-decisions-log.md) (chronologische Entscheidungshistorie, zuletzt D-042).

## Konventionen

- Dokumentation auf Deutsch (passend zum Brainstorming-Kontext)
- Code, Code-Kommentare und XML-Doc-Comments auf Englisch (passend zum Geef SDK)
- Entscheidungen werden im Decisions-Log dokumentiert, bevor sie in andere Dokumente einfließen
- Living Document: keine Versionsnummern, stattdessen "Letzte Aktualisierung" pro Datei