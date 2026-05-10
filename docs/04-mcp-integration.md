# MCP-Integration

*Letzte Aktualisierung: 10. Mai 2026*

## Warum MCP

Geef.Atelier soll nicht nur per Web-UI bedienbar sein, sondern auch von KI-Agenten konsumierbar. Der typische Anwendungsfall: ein Claude (oder ein anderer MCP-fähiger Client) arbeitet an einer komplexeren Aufgabe und braucht für einen Teilschritt einen besonders sorgfältig erzeugten Text. Statt diesen Text inline zu generieren — wo der aufrufende Claude weder mehrere Iterationen noch eine Reviewer-Crew zur Verfügung hat — delegiert er den Auftrag an Geef.Atelier, holt sich später das Ergebnis und arbeitet weiter.

MCP (Model Context Protocol) ist der Standard, mit dem solche Delegationen sauber laufen: Tools mit JSON-Schema-definierten Inputs/Outputs, einheitliche Auth, einheitlicher Transport.

## Architektonische Konsequenz

Der Service hat **zwei Frontends**: Web-UI und MCP-Server. Beide nutzen denselben Application-Service-Layer (`IRunService`). Die Pipeline-Logik, die Persistierung, das EventSink — all das ist Frontend-agnostisch.

```
Web-UI ──┐
         ├──> IRunService ──> Background-Orchestrator ──> Geef-Pipeline
MCP   ───┘
```

Daraus folgt: alles, was der User in der UI tun kann, soll auch via MCP tun können — bis auf rein UI-spezifische Dinge (Live-Stream-Anzeige).

## Tools, die der MCP-Server anbietet

### `submit_request`

Stellt einen neuen Auftrag in die Warteschlange.

**Input:**
```json
{
  "briefing": "string (required) — Beschreibung der Aufgabe und des gewünschten Ergebnisses",
  "options": {
    "executor_model": "string (optional) — z.B. 'claude-opus-4-7'",
    "reviewer_models": ["array (optional) — Liste von Modellen für die Reviewer"],
    "max_iterations": "int (optional, default 3)"
  }
}
```

**Output:**
```json
{
  "run_id": "uuid",
  "status": "Pending"
}
```

### `get_run_status`

Liefert den aktuellen Status eines Runs.

**Input:** `{ "run_id": "uuid" }`

**Output:**
```json
{
  "run_id": "uuid",
  "status": "Pending | Running | Completed | Failed | Aborted",
  "current_phase": "Grounding | Execution | Evaluation | Finalize | null",
  "current_iteration": 2,
  "tokens_used": 12345,
  "cost_total": 0.234,
  "started_at": "2026-05-10T12:00:00Z",
  "completed_at": null
}
```

### `get_run_result`

Liefert das fertige Ergebnis. Nur bei Status=Completed.

**Input:** `{ "run_id": "uuid" }`

**Output (bei Completed):**
```json
{
  "run_id": "uuid",
  "final_text": "string",
  "tokens_used": 12345,
  "cost_total": 0.234,
  "iteration_count": 2
}
```

**Output (bei anderem Status):** Error mit aktuellem Status, damit der Client weiß, ob er warten soll oder nicht.

### `list_runs`

Listet bestehende Runs.

**Input:**
```json
{
  "limit": "int (optional, default 20)",
  "status_filter": "string (optional)"
}
```

**Output:** Array von Run-Summaries (Id, Status, CreatedAt, BriefingPreview).

### `get_run_details`

Liefert den vollständigen Trail eines Runs.

**Input:** `{ "run_id": "uuid" }`

**Output:** Run-Daten plus alle Iterationen (mit Artefakt-Text), alle Findings, optional die letzten N Events.

### `cancel_run`

Bricht einen laufenden Run ab.

**Input:** `{ "run_id": "uuid" }`

**Output:** `{ "success": true | false, "message": "..." }`

## Transport

**Streamable HTTP** ist der moderne MCP-Transport-Standard und wird vom offiziellen [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk) unterstützt. Vorteil gegenüber dem älteren SSE-Transport: bidirektional über eine einzige Verbindung, leichter durch Reverse-Proxies zu führen, einfacheres Auth-Handling.

Endpunkt: `POST https://atelier.example.com/mcp/v1` (genauer Pfad wird im SDK festgelegt; üblich ist `/mcp`).

## Auth

Im Skeleton: **Bearer-Token** im `Authorization`-Header. Token wird aus Environment-Variable `ATELIER_MCP_TOKEN` gelesen. Kein Token-Refresh, kein Token-Rotation, ein Token reicht für den Single-User-Betrieb.

Später: OAuth-2.0-Flow nach MCP-Spec — der MCP-Standard definiert das, der C# SDK unterstützt es. Aktivierung erfolgt erst, wenn echter Bedarf entsteht (z.B. wenn mehrere Clients mit unterschiedlichen Berechtigungen anbinden).

## Beziehung zu Web-UI

Beide Frontends rufen denselben `IRunService` auf. Konsequenzen:

- Ein via MCP gestarteter Run erscheint sofort in der Web-UI (gleiche DB).
- Ein via UI gestarteter Run kann via MCP abgefragt werden.
- Status-Updates eines via MCP gestarteten Runs sind in der UI live sichtbar (SignalR-Stream).
- Cancellation funktioniert von beiden Seiten.

Das ist ein bewusstes Design-Ziel: ein Auftrag ist ein Auftrag, unabhängig vom Eintragsweg.

## Hosting

Im Skeleton läuft der MCP-Server **als Teil derselben ASP.NET-Anwendung** wie die Web-UI — derselbe Container, derselbe Prozess, eigener Pfad-Präfix (`/mcp`). Das spart Deployment-Aufwand. Sollte sich später Bedarf ergeben (z.B. unterschiedliche Skalierungs-Anforderungen), kann der MCP-Server in einen eigenen Container ausgegliedert werden, ohne dass sich Domain-Logik ändern muss.

## Discovery und Konfiguration

MCP-Clients (Claude Desktop etc.) erfahren von Geef.Atelier durch lokale Konfigurations-Datei. Beispiel-Eintrag für Claude Desktop:

```json
{
  "mcpServers": {
    "geef-atelier": {
      "url": "https://atelier.example.com/mcp",
      "transport": "streamable-http",
      "auth": {
        "type": "bearer",
        "token": "your-token-here"
      }
    }
  }
}
```

Die genaue Syntax hängt vom MCP-Client ab — Claude Desktop, Claude Code und Custom-Clients haben jeweils eigene Konfigurationsformate, aber das Prinzip ist überall gleich.

## Nicht im Skeleton

- OAuth-2.0-Auth (Bearer-Token reicht erstmal)
- Rate-Limiting (Single-User, kein Bedarf)
- Mehrere Token mit unterschiedlichen Berechtigungen
- Audit-Log MCP-spezifischer Aufrufe (alles läuft eh in den Events der Runs auf, das reicht)