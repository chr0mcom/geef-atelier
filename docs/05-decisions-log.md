# Decisions Log

*Letzte Aktualisierung: 10. Mai 2026*

Chronologisches Protokoll aller Entscheidungen aus dem Brainstorming. Format: Frage / Entscheidung / Begründung / ggf. Konsequenzen.

## 10. Mai 2026 — Erstes Brainstorming

### D-001: Erster Use-Case-Fokus

**Frage:** Mit welchem Use-Case fangen wir an?
**Optionen:** Juristischer Schriftsatz / Fachartikel / Generische Pipeline / Mehrere parallel
**Entscheidung:** Generische Pipeline ohne Domänen-Fokus.
**Begründung:** Verhindert, dass die Architektur einen Domänen-Bias einbacken bekommt. Spezialisierung (z.B. juristisch) kommt später als *Konfiguration* dazu — neue Reviewer-Profile, neue Crew-Templates — nicht als Code-Branch.
**Konsequenz:** Die Pipeline-Implementierung muss textsorten-agnostisch sein. Klassifikator denkt in Tags/Eigenschaften, nicht in fixen Kategorien.

### D-002: Mensch-im-Loop

**Frage:** Wieviel Mensch-Eingriff während eines laufenden Runs?
**Optionen:** Fire-and-Forget / Crew bestätigen dann F&F / Mehrere Eingriffspunkte / Maximale Kontrolle
**Entscheidung:** Reiner Fire-and-Forget (Start → Ergebnis).
**Begründung:** Simpelste Variante; keine Pause-Mechanik, kein Resume aus User-Sicht, keine UI-Interaktion mitten im Run.
**Konsequenz:** Crash-Recovery bleibt eine System-Anforderung (nicht User-Feature). Abbruch-Button in der UI bleibt drin als einziger User-Eingriff.

### D-003: Frontend-Stack

**Frage:** Welcher Frontend-Stack?
**Optionen:** Blazor Server (schnell) / React+API (flexibel) / Egal-mach-Vorschlag
**Entscheidung:** Blazor Server.
**Begründung:** Derselbe .NET-Stack wie Geef SDK, kein Kontextwechsel. SignalR ist eingebaut → Live-Status quasi gratis. Single-User → keine Skalierungs-Sorgen. Server-Hosting hinter Reverse-Proxy → typischer Blazor-Server-Nachteil (Roundtrip-Latenz) irrelevant. Falls später Wechsel zu Blazor WebAssembly oder React+API nötig: Backend-Logik bleibt unverändert.

### D-004: Datenbank

**Frage:** SQLite oder Postgres?
**Entscheidung:** Postgres.
**Begründung:** Anwendung wird im Docker auf Server gehostet, dort ist Postgres bereits etabliert. Kein zusätzlicher DB-Service nötig (außer für lokale Dev). Bonus: pgvector kann später für RAG genutzt werden, ohne separaten Vector-Store-Container.
**Konsequenz:** Solution nutzt Npgsql.EntityFrameworkCore.PostgreSQL. Connection-String aus Environment-Variable.

### D-005: MCP-Schnittstelle

**Frage:** Soll der Service auch per MCP ansprechbar sein?
**Entscheidung:** Ja — als zweites Frontend neben der Web-UI.
**Begründung:** Erlaubt KI-Agenten (Claude Desktop, Claude Code, Custom-Agents), Aufträge zu delegieren und Ergebnisse abzuholen. Nutzwert über reine UI hinaus.
**Konsequenz:**
- Application-Service-Layer (`IRunService`) wird zwingend, damit beide Frontends dieselbe Logik aufrufen.
- Eigenes Projekt `Geef.Atelier.Mcp` in der Solution.
- Auth-Strategie wird zweispurig: Cookie für UI, Bearer-Token für MCP.
- Bauplan wächst von 8 auf 10 Schritte.

### D-006: Projekt-Name

**Frage:** Wie heißt das Projekt?
**Entscheidung:** Geef.Atelier.
**Begründung:** "Atelier" passt thematisch (Werkstatt für hochwertige handwerkliche Arbeit), klingt nicht nach generischem SaaS, macht das Verhältnis zum SDK klar (das ist die Werkstatt, die das SDK nutzt).

### D-007: Bau-Konventionen (initial)

**Frage:** Welche Konventionen für Code und Kommunikation während der Umsetzung?
**Entscheidung (vorläufig):**
- Claude Code soll **selbst nach GEEF-Prinzip vorgehen**.
- Reviewer-Modell außerhalb der Anthropic-Familie (für echte Außenperspektive).
- Am Ende jedes Schritts: umfassender Abschlussbericht.
- Doku wird laufend in MD-Dateien im Projekt-Ordner gepflegt.
- Code/XML-Docs auf Englisch, Doku auf Deutsch.
**Status:** Durch D-009 konkretisiert und durch `geef_workflow.md` formalisiert.

### D-008: Reihenfolge der Schritte

**Frage:** Walking Skeleton oder erst tieferes Vorab-Brainstorming?
**Entscheidung:** Walking Skeleton zuerst.
**Begründung:** Schneller Realitätsabgleich zwischen Annahmen und Codebase. Themen wie Quellen-Handling, Multi-Provider-Adapter, Crew-Composition werden erst danach detailliert ausgearbeitet.

### D-009: Verbindlicher Workflow für Claude Code

**Frage:** Wie genau wird Claude Code arbeiten? Welche Reviewer, welche Advisor, welche Phasen?
**Entscheidung:** Es gibt eine **kanonische Workflow-Datei `geef_workflow.md`** auf Projekt-Ebene, die Claude Code strikt befolgt. Sie definiert:
- Vier Phasen (Grounding mit fünf Sub-Phasen, Execution mit On-Demand-Advisor, Evaluation mit Iteration-Advisor, Finalize mit Pre-Deploy-Advisor und Cleanup).
- Drei Rollen (Executor, Reviewer, Advisor) — nie verschmolzen.
- Fünf Reviewer:
  1. Functional Correctness (claude)
  2. Code Quality (codex mit gpt-5.4)
  3. Test Execution Verifier (claude)
  4. Architecture Compliance (claude)
  5. Live UI Verification via Playwright MCP
- Pflicht-Advisors: Pre-Mortem (strategic), Devil's Advocate (critical), Iteration-Advisor (ab Iter. 2), Pre-Deploy-Advisor (vor Production-Release).
- Hard Rules: Architektur ist binding, Tests sind Pflicht, eigene UI-Components zwingend, max. 15 Iterationen, Stagnation-Detection bei drei gleichen Findings hintereinander.

**Konsequenz für die Brainstorming-Doku:**
- Bau-Prompts werden drastisch kürzer — sie verweisen nur noch auf den Workflow und ergänzen den schritt-spezifischen Scope.
- Berichte für den Brainstorming-Chat werden in `geef-atelier-docs/reports/` abgelegt — das überlebt das Phase-4.3-Cleanup, weil der Pfad nicht der `geef_*.md`-Naming-Konvention entspricht.
- D-007 wird durch diese Entscheidung formalisiert; das ursprüngliche "GPT 5.5 als Reviewer" ist durch den Workflow auf "gpt-5.4 in Reviewer 2" konkretisiert.