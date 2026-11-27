# Vision und Scope

*[English](01-vision-and-scope.md) · **Deutsch***

*Letzte Aktualisierung: 2026-11-27 (D-091: Etappe 8d, der Umfang entspricht dem aktuellen Produkt: nur der Standardweg, kein Zusammenstellen von Teams, kein Lernen, keine Wissensbasis; zuvor 2026-11-26: Leitfäden je Zielgruppe und die Seite „How it works“; D-077; D-068: höchstens drei kurze Rückfragen mit Klickantworten vor dem Lauf, nie währenddessen; 22. Mai 2026, D-054)*

## Vision

**Geef.Atelier** ist eine Text-Manufaktur: Aus dem Anliegen und den Dateien einer Person wird ein fertiger Text, und das
Atelier sagt ehrlich, was geprüft wurde und was noch offen ist. Ein Anliegen wird ein **Auftrag**; höchstens drei kurze
Fragen kommen vor dem Start; ein Schreibaufruf bekommt die Dateien im Volltext; danach folgen automatische Prüfungen und
eine Faktenprüfung durch ein anderes Modell, mit höchstens einer Korrektur. Einen zweiten, langsameren Weg gibt es nicht. Die
Leitfäden [12](12-user-guide_de.md) bis [15](15-developer-guide_de.md) beschreiben das Produkt je Zielgruppe.

Die ursprüngliche Vision war eine Maschine, die für jedes Werkstück eine Crew aus KI-Rollen zusammenstellt. Diese
Arbeitsweise wurde in Etappe 8d (D-091) entfernt, zusammen mit der Learning-Schleife und der Wissensbasis; die Git-Historie
und das [Decisions-Log](05-decisions-log_de.md) bewahren ihre Beschreibung.

Das Projekt ist die produktive Anwendung des [Geef SDK](https://github.com/chr0mcom/geef): Es nutzt das
GEEF-Pipeline-Pattern (Grounding / Execution / Evaluation / Finalize), seine Convergence-Policies, Evaluation-Strategies,
den EventSink und die Middleware.

## Leitsterne

1. **Ein klarer Weg** — jeder Auftrag geht denselben Weg. Es gibt keine Teams, Profile oder Pipelines einzurichten.
2. **Ehrliche Transparenz** — jedes Ergebnis kommt mit einem festen Bericht, was geprüft wurde, den offenen Punkten und den
   Annahmen; jeder Lauf behält seine Durchgänge und Befunde.
3. **Modell-Pluralismus** — die Prüfungen nutzen bewusst andere Modelle als der Schreiber, um eine echte Außenperspektive
   statt Selbstbestätigung zu bekommen.

## In Scope

- Texte, die Menschen geschrieben haben wollen: Briefe, E-Mails, Artikel, Zusammenfassungen, Berichte und Ähnliches, aus Anliegen und Dateien
- Mehrbenutzer-Hosting auf eigenem Docker-Server: DB-basierte Benutzerverwaltung (BCrypt), Aufträge und Läufe pro Nutzer isoliert, Admin-Override per expliziten Umschaltern
- Web-UI (Blazor Server) für Aufträge: Startseite, „My work“, Auftragsseite, „About me“
- MCP-Server-Schnittstelle, damit KI-Assistenten (Claude Desktop, Claude Code, Custom-Agents) Aufträge erteilen und Ergebnisse abholen können, mit statischem Bearer-Token oder dem selbst gehosteten OAuth 2.1
- LLM-Provider: Abo-CLIs (Claude Code, Codex) über einen lokalen Proxy und OpenRouter (Pay-per-Token); die Prüfungen laufen bewusst auf anderen Modellen als der Schreiber
- Dateien als Quellen: Upload von PDF, Word, Markdown oder reinem Text, auch über ein Upload-Ticket aus einem KI-Assistenten; der Schreiber bekommt sie im Volltext
- Web-Recherche für Aufträge ohne Dateien, wo eingeschaltet (Tavily), gezeigt als Stellen anderer Webseiten, nicht als geprüfte Tatsachen
- Fire-and-Forget-Workflow: Auftrag erteilen, später nachsehen, Ergebnis abholen. **Vor** dem Schreiben sind höchstens drei kurze Rückfragen mit fertigen Antworten zum Anklicken erlaubt, immer überspringbar (D-068); während des Schreibens gibt es keinen Eingriff
- Einen fertigen Text als neue Version desselben Auftrags ändern, mit Vergleich der Versionen
- Download des Textes jedes Auftrags als Word, PDF oder Markdown, auf der Auftragsseite und über MCP (`export_order`, D-070, D-075)
- Persistente Historie von Aufträgen und Läufen; einen fehlgeschlagenen oder abgebrochenen Lauf des Standardwegs fortsetzen (D-046)

## Out of Scope (vorerst)

- Echte Mandantenfähigkeit / Multi-Tenant-Isolation (das System ist Mehrbenutzer, aber single-tenant: ein Admin, gemeinsame Konfiguration; pro Nutzer sind nur Aufträge und Läufe isoliert)
- Öffentlicher Zugang ohne Auth
- Mensch-im-Loop während des Schreibens. Nur **vor** dem Schreiben darf das Atelier höchstens drei kurze Rückfragen mit Klickantworten stellen, und nur dann, wenn sich die Antwort weder aus Anliegen, Dateien oder Profil entnehmen noch recherchieren noch vernünftig annehmen lässt, der Anwender sie wissen kann und sie das Ergebnis wesentlich ändert (D-068)
- Das Zusammenstellen von Teams aus KI-Rollen, eigene Pipelines oder Werkzeugnutzung durch Modelle
- Lernen über Aufträge hinweg, ein Gedächtnis je Nutzer über „About me“ hinaus und eine Wissensbasis (Vektorsuche)
- Abschlussschritte, die einen Text umwandeln oder anderswohin schicken
- Mobile Apps oder native Clients
- Kommerzielles Hosting, Billing, Abrechnung
- Domänen-spezifische Datenbank-Connectors (zum Beispiel juristische Datenbanken)

## Zielnutzer

Mehrere benannte Benutzerkonten, verwaltet durch einen Admin. Die Anwendung ist nicht mandantenfähig (keine Tenant-Trennung der Konfiguration), aber jeder Nutzer sieht nur seine eigenen Aufträge und Läufe; der Admin kann optional alles einsehen. Authentifizierung ist Pflicht, weil die App über das öffentliche Internet erreichbar ist (Server-Hosting).

## Hosting-Umgebung

- Docker-Container auf eigenem Server (Docker Compose): Web-App, cli-proxy, Postgres und ein Backup-Dienst
- Reverse-Proxy (Traefik / Nginx) übernimmt TLS-Terminierung
- API-Keys, Connection-Strings, Auth-Secrets über Environment-Variablen / Docker Secrets
- Persistente Daten in Postgres; hochgeladene Dateien auf einem Volume

## Erfolgs-Kriterium für das Skeleton

> **Historisch.** Das war zu Projektbeginn die Definition des Minimal-Ziels; es wurde im Mai 2026 erreicht. Es nennt zwei
> feste Reviewer; so arbeitet das Produkt heute nicht (siehe [Vision](#vision)).

Das Walking Skeleton (siehe [03-walking-skeleton-plan.md](03-walking-skeleton-plan_de.md)) war erfolgreich, wenn:

1. Ein Auftrag mit reinem Text-Briefing über die Web-UI startbar war.
2. Die Pipeline mit Anthropic als Executor und zwei festen Reviewern (z.B. mit OpenAI-Modell) durchlief, ohne dass der User eingreifen musste.
3. Während des Runs ein Live-Status in der UI sichtbar war (aktuelle Phase, Iteration, Findings).
4. Nach Abschluss der finale Text in der UI angezeigt wurde.
5. Derselbe Workflow auch über den MCP-Server funktionierte (Auftrag absetzen, Status abfragen, Ergebnis abholen via MCP-Tools).
6. Die ganze Anwendung als Docker-Container deploybar war und sich an Postgres anband.
