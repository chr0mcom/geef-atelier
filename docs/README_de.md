# Geef.Atelier — Projektdokumentation

*[English](README.md) · **Deutsch***

Dies ist die laufend gepflegte Doku zum Projekt **Geef.Atelier**: Es macht aus einem kurzen Auftrag einen fertigen Text und sagt, was geprüft wurde, was noch offen ist und was angenommen wurde. Grundlage ist das [Geef SDK](https://github.com/chr0mcom/geef).

**Neu hier?** Fang mit dem Leitfaden für deine Rolle an:

| Leitfaden | Für |
|---|---|
| [12-user-guide_de.md](12-user-guide_de.md) | Alle, die im Browser mit Atelier schreiben |
| [13-ai-assistant-guide_de.md](13-ai-assistant-guide_de.md) | Menschen, die Atelier aus einem KI-Assistenten nutzen (MCP) |
| [14-admin-guide_de.md](14-admin-guide_de.md) | Administration: Konten, Schalter, Anbieter |
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
| [06-reviewer-calibration.md](06-reviewer-calibration_de.md) | Severity-Taxonomie und Convergence-Policy; die Teile zur alten Prüfschleife sind als historisch gekennzeichnet (in D-091 entfernt) | Living |
| [07-design-system.md](07-design-system_de.md) | Theme-System, Typografie, Komponenten-Inventar (Stand Design-Translation PS-3) | Living |
| [08-crew-system.md](08-crew-system_de.md) | Was vom Crew-System bleibt: die eine Aufstellung im Code, Crew-Snapshot, Modellmatrix, Prüfer des Direktwegs; die entfernten Teile sind aufgeführt (D-091) | Living |
| [09-endpoint-reference.md](09-endpoint-reference_de.md) | Referenz aller extern erreichbaren HTTP-Endpunkte (MCP, OAuth, Web/Account) | Living |
| [10-tool-system.md](10-tool-system_de.md) | Kurzer Hinweis: Das Tool-System wurde in Stufe 8d entfernt (D-091); die Allowlist für Umgebungsvariablen gilt weiter | Hinweis |
| [11-specialization-packs.md](11-specialization-packs_de.md) | Kurzer Hinweis: Die Spezialisierungs-Packs wurden in Stufe 8d entfernt (D-091) | Hinweis |
| [ops/atelier-smoke.md](ops/atelier-smoke.md), [ops/atelier-watch.md](ops/atelier-watch.md) | Betrieb: Mini-Lauf nach dem Deploy, Wächter für hängende Läufe (nur Deutsch) | Living |

> **Interne Arbeitsverzeichnisse (nicht im öffentlichen Repository):**
> `docs/prompts/` (schritt-spezifische Bau-Prompts) und `docs/reports/` (persistente
> Abschlussberichte je Bau-Schritt) sind bewusst über `.gitignore` ausgeschlossen und
> existieren nur in der lokalen Arbeitskopie. Verweise auf `reports/…` bzw. `prompts/…`
> in den historischen Dokumenten dienen der Provenienz und sind auf GitHub nicht
> auflösbar — das ist beabsichtigt.

## Entwicklungs-Workflow

Änderungen entstehen in Phasen (Grounding, Execution, Evaluation, Finalize) mit unabhängigen Prüfungen; die Bau-Prompts und
Berichte dieses Ablaufs liegen in den oben genannten, nicht veröffentlichten Arbeitsordnern.

## Status

- **Phase:** produktiv. Atelier schreibt jeden Text auf einem Weg: dem Auftrag mit dem Direktweg (D-068 bis D-077). Die Variante „Extra careful“ wurde nach ihrer Messung entfernt (zweiter Zusatz zu D-087). Das ältere Crew-System und alles, was nur es nutzte (Werkstatt, Tools, Lernen, Wissensbasis, Abschlussschritte, Advisor, Packs), wurden in Stufe 8d entfernt (D-091).
- **Letzte Aktualisierung:** 2026-11-27
- **Aktueller Stand & nächste Schritte:** siehe [Projekt-README](../README_de.md) (Implementierungsstand) und [Decisions-Log](05-decisions-log_de.md) (chronologische Entscheidungshistorie, zuletzt D-092: Die Datenbank wurde zu einer Baseline-Migration aufgeräumt, nachdem Stufe 8d, D-091, den Rest der älteren Arbeitsweise entfernt hatte). Die Dokumente kennzeichnen, was entfernt wurde.

## Konventionen

- Dokumentation zweisprachig: die englische Datei (`X.md`) ist die führende Version, das deutsche Original bleibt als `X_de.md` erhalten; beide werden synchron gehalten
- Code, Code-Kommentare und XML-Doc-Comments auf Englisch (passend zum Geef SDK)
- Entscheidungen werden im Decisions-Log dokumentiert, bevor sie in andere Dokumente einfließen
- Living Document: keine Versionsnummern, stattdessen "Letzte Aktualisierung" pro Datei
