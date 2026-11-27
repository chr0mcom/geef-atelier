# Geef.Atelier — Projektdokumentation

*[English](README.md) · **Deutsch***

Dies ist die laufend gepflegte Doku zum Projekt **Geef.Atelier**: Es macht aus einem kurzen Auftrag einen fertigen Text und sagt, was geprüft wurde, was noch offen ist und was angenommen wurde. Grundlage ist das [Geef SDK](https://github.com/chr0mcom/geef).

**Neu hier?** Fang mit dem Leitfaden für deine Rolle an:

| Leitfaden | Für |
|---|---|
| [12-user-guide_de.md](12-user-guide_de.md) | Alle, die im Browser mit Atelier schreiben |
| [13-ai-assistant-guide_de.md](13-ai-assistant-guide_de.md) | Menschen, die Atelier aus einem KI-Assistenten nutzen (MCP) |
| [14-admin-guide_de.md](14-admin-guide_de.md) | Administration: Konten, Schalter, die ältere Werkstatt |
| [15-developer-guide_de.md](15-developer-guide_de.md) | Entwicklung und Betrieb: Karte des Systems, Tests, Regeln |

Die laufende Anwendung erklärt sich außerdem auf der öffentlichen Seite `/how-it-works`, die den aktuellen Schaltern folgt.

Die Doku entsteht parallel zum Brainstorming und wird nach jeder Entscheidungsrunde aktualisiert. Wenn ein Thema noch nicht ausgearbeitet ist, steht das explizit im jeweiligen Dokument.

## Dokumentenstruktur

| Datei | Inhalt | Typ |
|---|---|---|
| [12-user-guide.md](12-user-guide_de.md) … [15-developer-guide.md](15-developer-guide_de.md) | Leitfäden je Zielgruppe (siehe oben) | Living |
| [01-vision-and-scope.md](01-vision-and-scope_de.md) | Was ist das Projekt, was nicht, für wen, in welchem Umfeld | Living |
| [02-architecture.md](02-architecture_de.md) | Schichten, Komponenten, Datenfluss, DB-Schema; Schutzmechanismen am Lauf, Konfigurationsschalter, Ende-zu-Ende-Gerüst (D-067) | Living |
| [03-walking-skeleton-plan.md](03-walking-skeleton-plan_de.md) | Der ursprüngliche 10-Schritte-Bauplan (historisch; Skeleton abgeschlossen) | Historisch |
| [04-mcp-integration.md](04-mcp-integration_de.md) | MCP-Server: Tools, Auth (Bearer + OAuth 2.1), Transport | Living |
| [05-decisions-log.md](05-decisions-log_de.md) | Chronologisches, append-only Protokoll aller Architektur-Entscheidungen (D-001 ff.) | Historisch |
| [06-reviewer-calibration.md](06-reviewer-calibration_de.md) | Severity-Taxonomie und Convergence-Policy-Standard für Reviewer | Living |
| [07-design-system.md](07-design-system_de.md) | Theme-System, Typografie, Komponenten-Inventar (Stand Design-Translation PS-3) | Living |
| [08-crew-system.md](08-crew-system_de.md) | Crew-/Profil-/Template-System, System-Profile, Advisor-Pässe – der ältere Weg („Workshop“, „Extra careful“) | Living |
| [09-endpoint-reference.md](09-endpoint-reference_de.md) | Referenz aller extern erreichbaren HTTP-Endpunkte (MCP, OAuth, Web/Account) | Living |
| [10-tool-system.md](10-tool-system_de.md) | Zentraler Tool-Katalog + agentischer Tool-Use (Pull) + Grounding (Push) + MCP-Client (D-060); Ist-Zustand: Pull nie verdrahtet und eingefroren, Allowlist für Umgebungsvariablen (D-067) | Living |
| [11-specialization-packs.md](11-specialization-packs_de.md) | Generische Akteure + wiederverwendbare gescopte Spezialisierungs-Packs (D-061) | Living |
| [ops/atelier-smoke.md](ops/atelier-smoke.md), [ops/atelier-watch.md](ops/atelier-watch.md) | Betrieb: Mini-Lauf nach dem Deploy, Wächter für hängende Läufe (nur Deutsch) | Living |

> **Interne Arbeitsverzeichnisse (nicht im öffentlichen Repository):**
> `docs/prompts/` (schritt-spezifische Bau-Prompts) und `docs/reports/` (persistente
> Abschlussberichte je Bau-Schritt) sind bewusst über `.gitignore` ausgeschlossen und
> existieren nur in der lokalen Arbeitskopie. Verweise auf `reports/…` bzw. `prompts/…`
> in den historischen Dokumenten dienen der Provenienz und sind auf GitHub nicht
> auflösbar — das ist beabsichtigt.

## Verbindlicher Entwicklungs-Workflow

Im übergeordneten Infrastruktur-Repository unter `/srv/docker/docs/` liegen die übergreifenden Prozess-Vorgaben, allen voran **`geef-workflow.md`** — die kanonische Anweisung an Claude Code, *wie* gebaut wird (außerhalb dieses Repositories, daher hier nicht verlinkt). Sie definiert die vier Phasen (Grounding / Execution / Evaluation / Finalize), die fünf Reviewer und die Advisor-Konsultationen. Alle Bau-Prompts in `docs/prompts/` verweisen darauf und ergänzen nur den schritt-spezifischen Scope. Die weiteren Dateien in jenem Ordner (z.B. `docker-deployment.md`, `contact-protection.md`) sind ebenfalls verbindlich und müssen beachtet werden.

## Status

- **Phase:** produktiv. Seit dem Umbau im November 2026 (D-068 bis D-077) ist der Auftrag mit dem Direktweg der Standard; das ältere Crew-System (mit seinen Erweiterungen wie Advisor-Pässen, Grounding, Finalizern und der pausierten Lernschleife; Template Studio wurde in Stufe 8c entfernt, D-089) bleibt als „Workshop“ und „Extra careful“ verfügbar.
- **Letzte Aktualisierung:** 2026-11-26
- **Aktueller Stand & nächste Schritte:** siehe [Projekt-README](../README_de.md) (Implementierungsstand) und [Decisions-Log](05-decisions-log_de.md) (chronologische Entscheidungshistorie, zuletzt D-089 „Rückbau, Schnitt 1“). Der Umbau nach D-068 endet mit Etappe 6 und der Abschlussrunde 8a (D-077): Die Etappen 1b, 7, 8 (außer 8a) und 9 sind nicht gebaut, und das alte Crew-, Tool- und Learning-System bleibt bestehen, bis der Auslöser für den Rückbau in D-077 erfüllt ist; der erste Schnitt des Rückbaus (Stufe 8c, D-089) hat die ungenutzten Teile entfernt; die Dokumente kennzeichnen eingefrorene oder zum Rückbau vorgesehene Teile.

## Konventionen

- Dokumentation zweisprachig: die englische Datei (`X.md`) ist die führende Version, das deutsche Original bleibt als `X_de.md` erhalten; beide werden synchron gehalten
- Code, Code-Kommentare und XML-Doc-Comments auf Englisch (passend zum Geef SDK)
- Entscheidungen werden im Decisions-Log dokumentiert, bevor sie in andere Dokumente einfließen
- Living Document: keine Versionsnummern, stattdessen "Letzte Aktualisierung" pro Datei
