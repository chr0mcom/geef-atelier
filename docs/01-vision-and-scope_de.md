# Vision und Scope

*[English](01-vision-and-scope.md) · **Deutsch***

*Letzte Aktualisierung: 17. Mai 2026 (Scope an Mehrbenutzer-Stand angeglichen)*

## Vision

**Geef.Atelier** ist eine Text-Manufaktur: ein Atelier aus spezialisierten KI-Rollen (Executor, Reviewer, optional Advisor), das je nach Werkstück unterschiedlich besetzt wird. Der Unterschied zu einem klassischen "GPT-Wrapper" ist nicht Bedienung oder Preis, sondern dass die Maschine *vor der Arbeit entscheidet, wie sie arbeitet* (adaptive Crew-Komposition) und *während der Arbeit transparent macht, was sie tut* (Prozess-Sichtbarkeit über Iterations- und Findings-Trail).

Das Projekt ist die produktive Anwendung des [Geef SDK](https://github.com/chr0mcom/geef): Es nutzt das GEEF-Pipeline-Pattern (Grounding / Execution / Evaluation / Finalize) und die Erweiterungen (Convergence-Policies, Evaluation-Strategies, Advisor-Pattern, EventSink, Middleware), um Texte in Höchstqualität zu erzeugen.

## Leitsterne

1. **Adaptive Crew-Komposition** — Der Klassifikator erkennt die Textsorte und stellt aus einem getaggten Reviewer-/Advisor-Pool die passende Crew zusammen. Spezialisierung passiert über Daten (Reviewer-Profile, Crew-Templates), nicht über Code-Branches.
2. **Prozess-Transparenz** — Jeder Run hinterlässt einen vollständigen Trail: alle Iterationen, alle Findings, alle Advisor-Konsultationen. Vertrauen entsteht durch Nachvollziehbarkeit.
3. **Modell-Pluralismus** — Reviewer nutzen bewusst andere Modelle als der Executor, um echte Außenperspektive zu erzeugen statt Selbstbestätigung.

## In Scope

- Generische Pipeline für beliebige Textsorten (juristische Schriftsätze, Fachartikel, Marketing, akademische Texte, Briefe, Gedichte, …)
- Mehrbenutzer-Hosting auf eigenem Docker-Server: DB-basierte Benutzerverwaltung (BCrypt), Run-Sichtbarkeit pro Nutzer isoliert, Admin-Override per expliziten Umschaltern
- Web-UI (Blazor Server) für Auftragserteilung, Live-Status, Ergebnis-Abholung
- MCP-Server-Schnittstelle, damit externe KI-Clients (Claude Desktop, Claude Code, Custom-Agents) Aufträge erteilen und Ergebnisse abholen können — Auth über statisches Bearer-Token oder self-hosted OAuth 2.1
- Multi-Provider-LLM-Support: OpenRouter (Pay-per-Token) sowie Subscription-CLIs (Claude Code, Codex) über einen lokalen Proxy; Reviewer/Advisor bewusst mit Fremd-Modellen
- Quellen-Übergabe in mehreren Formen: Datei-Upload (PDF, DOCX, TXT, MD), URLs, Freitext-Briefing, Stil-Referenztexte; semantische Wissensbasis (Vector-Store-RAG) und Run-lokale Attachments
- Fire-and-Forget-Workflow: Auftrag starten, später Status prüfen, am Ende Ergebnis abholen — keine Mensch-im-Loop-Eingriffe während des Runs
- Persistente Run-Historie mit vollständigem Iterations-Trail

## Out of Scope (vorerst)

- Echte Mandantenfähigkeit / Multi-Tenant-Isolation (das System ist Mehrbenutzer, aber single-tenant: ein Admin, gemeinsame Konfiguration; pro Nutzer ist nur die Run-Sichtbarkeit isoliert)
- Öffentlicher Zugang ohne Auth
- Mensch-im-Loop zwischen Iterationen (bewusst nicht implementiert; Fire-and-Forget bleibt das Modell)
- Mobile Apps oder native Clients
- Kommerzielles Hosting, Billing, Abrechnung
- Echte Memory-Backed-Advisors mit Cross-Run-Lernen
- Domänen-spezifische Datenbank-Connectors (z.B. juristische Datenbanken wie dejure.org oder Beck-Online) — später optional
- **Export** jenseits von Markdown: DOCX-/PDF-*Ausgabe* ist weiterhin out of scope. (Hinweis: PDF/DOCX/TXT/MD als *Eingabe*/Quelle ist umgesetzt — siehe „In Scope“.)

## Zielnutzer

Mehrere benannte Benutzerkonten, verwaltet durch einen Admin. Die Anwendung ist nicht mandantenfähig (keine Tenant-Trennung von Konfiguration oder Crew-Daten), aber jeder Nutzer sieht nur seine eigenen Runs; der Admin kann optional alles einsehen. Authentifizierung ist Pflicht, weil die App über das öffentliche Internet erreichbar ist (Server-Hosting).

## Hosting-Umgebung

- Docker-Container auf eigenem Server
- Postgres ist bereits als Datenbankdienst auf dem Server vorhanden — das Projekt nutzt diese existierende Postgres-Instanz, kein separater DB-Container nötig (außer für lokale Entwicklung)
- Reverse-Proxy (Traefik / Nginx) übernimmt TLS-Terminierung
- API-Keys, Connection-Strings, Auth-Secrets über Environment-Variablen / Docker Secrets
- Persistente Datenhaltung in Postgres (inkl. pgvector für die semantische Wissensbasis); hochgeladene Quellen werden indexiert in der Datenbank gehalten

## Erfolgs-Kriterium für das Skeleton

> **Status:** Dieses Skeleton-Erfolgskriterium ist seit Mai 2026 vollständig erfüllt; die App läuft produktiv. Die unten als „danach“ genannten Erweiterungen (Quellen-Upload/RAG, Crew-Komposition, Advisor-Pässe) sind inzwischen ausgeliefert. Die folgende Liste bleibt als ursprüngliche Definition des Minimal-Ziels erhalten.

Das Walking Skeleton (siehe [03-walking-skeleton-plan.md](03-walking-skeleton-plan_de.md)) ist erfolgreich, wenn:

1. Ein Auftrag mit reinem Text-Briefing über die Web-UI startbar ist.
2. Die Pipeline mit Anthropic als Executor und zwei festen Reviewern (z.B. mit OpenAI-Modell) durchläuft, ohne dass der User eingreifen muss.
3. Während des Runs ein Live-Status in der UI sichtbar ist (aktuelle Phase, Iteration, Findings).
4. Nach Abschluss der finale Text in der UI angezeigt wird.
5. Derselbe Workflow auch über den MCP-Server funktioniert (Auftrag absetzen, Status abfragen, Ergebnis abholen via MCP-Tools).
6. Die ganze Anwendung als Docker-Container deploybar ist und sich an die existierende Postgres-Instanz anbindet.

Alles weitere (Quellen-Upload, Klassifikator, dynamische Crew, Advisor, Multi-Format-Export) sind Erweiterungen *nach* dem Skeleton.
