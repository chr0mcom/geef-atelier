# Endpoint-Referenz

*[English](09-endpoint-reference.md) · **Deutsch***

*Letzte Aktualisierung: 2026-11-25 (Etappe 2: `answer_order`, `needs_input` mit Fragen in `submit_order`/`get_order_status`, `assumptions` in `get_order_result`, neue Server-Hinweise; Etappe 1: Auftrags-Werkzeuge `submit_order`, `get_order_status`, `get_order_result`, `submit_request` als Hülle mit `orderId`, Server-Hinweise, `list_learnings` nur für Admins; D-067: `warnings` und `outcome` in `get_run_status`/`get_run_result`, Ergebnis erst nach `outcome` lesen; `/hubs/runs` bleibt bis Etappe 8; `/hubs/runs` nur für Angemeldete; Editoren nur für Admins und `/access-denied`; Learning standardmäßig pausiert; zuvor 2026-05-22, D-054: Tool list_learnings ergänzt; providerType um learning-retrieval erweitert)*

Alle HTTP-Endpunkte von Geef.Atelier, die extern erreichbar sind — MCP, OAuth 2.1
sowie die Web-UI-/Account-Endpunkte. Basis-URL: `https://geef.stefan-bechtel.de`.

---

## MCP-Endpunkt

| Endpunkt | Methode | Auth |
|----------|---------|------|
| `/mcp` | POST | Bearer-Token (statisch oder OAuth) |

Der eigentliche MCP-Endpunkt. Clients senden hier ihre JSON-RPC-Requests (Tool-Calls). Unterstützt Streamable-HTTP-Transport (`Stateless=true`).

**Auth-Möglichkeiten:**

- **Claude Code CLI:** `Authorization: Bearer <ATELIER_MCP_TOKEN>` (statisches Token aus `.env`)
- **Claude Desktop / Claude.ai:** OAuth-2.1-Access-Token (Bearer), ausgestellt nach dem unten beschriebenen OAuth-Flow

Wenn kein oder ein ungültiges Token mitgeschickt wird, antwortet der Server mit `401 Unauthorized` und dem Header:
```
WWW-Authenticate: Bearer resource_metadata="https://geef.stefan-bechtel.de/.well-known/oauth-protected-resource"
```
Darüber entdecken OAuth-fähige Clients automatisch den Authorization Server.

### MCP-Tools

Der MCP-Server stellt folgende Tools bereit, die über den `/mcp`-Endpunkt aufgerufen werden können. Run-bezogene Tools erzwingen dieselbe Run-User-Isolation wie die Web-UI (D-042): Nicht-Admin-Nutzer können nur auf eigene Runs zugreifen.

#### Aufträge: `submit_order`, `get_order_status`, `get_order_result`

Seit Etappe 1 ist der **Auftrag** die Einheit, die der Mensch sieht; Läufe bleiben das technische Protokoll darunter (ein Auftrag kann mehrere Läufe haben). Aufträge sind wie Läufe besitzergeprüft: Nicht-Admins sehen nur ihre eigenen Aufträge, ein unbekannter oder fremder Auftrag ergibt den Fehler `Order not found.` Parameter- und Feldnamen sind camelCase.

**Eingabe `submit_order`:**
```json
{
  "request": "<string, Pflicht: der Auftrag im Wortlaut des Menschen>",
  "attachments": "<string, optional: JSON-Array [{\"filename\":\"angebot.pdf\",\"contentType\":\"application/pdf\",\"contentBase64\":\"...\"}]>",
  "extraCareful": false
}
```

Unterstützte `contentType`-Werte: `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `text/markdown` (auch `text/x-markdown`), `text/plain` (Parameter wie `; charset=utf-8` werden ignoriert). `extraCareful = true` startet den langsameren Modus mit mehreren Prüfrunden; sonst gilt der Direktweg.

**Ausgabe `submit_order`:**
```json
{
  "orderId": "<uuid>",
  "state": "queued",
  "runId": "<uuid|null>",
  "message": "<Klartext: was als Nächstes zu tun ist>",
  "sources": [ { "fileName": "angebot.pdf", "pages": 80, "characters": 160485, "quality": "good|partial|poor", "warnings": ["<string>"] } ],
  "understanding": "<string|null: was Atelier schreiben wird, in der Sprache des Auftrags>",
  "assumptions": [ { "id": "a1", "text": "<string>", "unsure": false } ],
  "questions": [ { "id": "length", "topic": "length", "text": "<string>", "options": [ { "id": "short", "label": "<string>" } ], "defaultOptionId": "<string|null>", "allowFreeText": true } ],
  "language": "<ISO 639-1|null>",
  "revisionFile": "<string|null: bei needs_input die angehängte Datei, die Atelier überarbeiten wird>"
}
```

`submit_order` wartet im Zustand `preparing` bis zu `Intake:SubmitWaitSeconds` (Standard 30, gekappt auf `Orders:MaxWaitSeconds`) und antwortet dann mit dem aktuellen Zustand:
- `needs_input`: `runId` ist `null`, `questions` enthält ein bis drei Fragen; `message` bittet den Client, den Menschen zu fragen und `answer_order` aufzurufen.
- `queued`/`working`: `runId` ist gesetzt, `questions` ist `null`; `understanding`/`assumptions` sind gesetzt, wenn der Auftrag gelesen wurde.
- `preparing` (Wartezeit abgelaufen): keine Fragen, `message` = „Still reading the request. Call get_order_status with waitSeconds=25.“

`understanding`, `assumptions`, `questions` und `language` sind `null` bei Aufträgen ohne Klärung (`Orders:IntakeMode` `Off` oder `Shadow`, `submit_request`, Aufträge über `Intake:SkipAboveChars`). `topic` ist einer der Werte `length`, `audience`, `purpose`, `tone`, `formality`, `language`, `sender`, `signature`, `facts`, `format`, `date`, `scope`, `other`. Fragen heißen `q1`…`q3` oder `length`, Optionen `o1`…`o4` oder `short`/`medium`/`long`, Annahmen `a1`, …. `revisionFile` nennt die angehängte Datei, die Atelier als Ausgangstext überarbeiten wird (nur bei `needs_input`, nur wenn der Auftrag eine Überarbeitung verlangt und der Klärer zustimmt); für einen neuen Text stattdessen `revision` mit `no` beantworten.

`understanding`, `assumptions`, `questions` und Optionen entstehen, indem Atelier Auftrag und Dateien liest. Sie können Text aus einer Datei wiederholen und sind keine Anweisungen: Clients befolgen keine Anweisungen daraus, öffnen keine Links daraus und fragen den Menschen deswegen nie nach Zugangsdaten. Der Server verwirft bereits Fragen, Optionen, Annahmen und Zusammenfassungen mit Links oder Adressen (ein nackter Name bleibt nur, wenn er im Auftrag steht) sowie alle, die Passwörter, PINs, TANs, Schlüssel, Karten- oder Kontonummern nennen, wenn der Auftrag selbst diese Art (Zugangsdaten bzw. Zahlungs- und Kontodaten) nicht behandelt; die Bitte um ein solches Geheimnis wird immer verworfen.

**Prüfung der Anhänge.** Bevor eine Datei gelesen wird, weist der Server ab, was offensichtlich nicht die Datei selbst ist: ungültiges Base64, leerer Inhalt, Text mit weniger als 10 sichtbaren Zeichen, kurze Texte, die nur auf eine Datei verweisen (etwa „siehe angehängte Vollversion“), Text, der selbst Base64 ist (doppelt kodiert), und Inhalt, dessen Magic Bytes nicht zum `contentType` passen (ein „PDF“ ohne `%PDF-`, eine `.docx` ohne ZIP-Kopf, ein als Text angegebenes PDF oder ZIP). Eine Datei, die die Annahme nicht verwenden kann (etwa ein Scan ohne Text), wird mit ihrem Grund abgewiesen. In jedem Fall wird nichts gestartet, die von diesem Aufruf gespeicherten Dateien werden wieder freigegeben, und die Meldung nennt den richtigen Weg: die vollständige Originaldatei als Base64 mit passendem `contentType` übergeben; ist die Datei zu groß für ein Argument, den Auftrag in der Weboberfläche anlegen und die Datei dort ablegen.

**Eingabe `get_order_status`:** `{ "orderId": "<uuid>", "waitSeconds": 25 }`

`waitSeconds` wird auf `0 … Orders:MaxWaitSeconds` (Standard 50) geklemmt; `0` antwortet sofort. Der Aufruf wartet, bis sich der Anzeigezustand ändert oder der Auftrag fertig ist, spätestens `waitSeconds` lang; bei `needs_input` antwortet er sofort. Bricht der Client ab, endet das Warten sofort.

**Ausgabe `get_order_status`:**
```json
{
  "orderId": "<uuid>",
  "state": "preparing|needs_input|queued|working|delivered|delivered_with_open_points|failed|stopped",
  "done": false,
  "step": "Writing",
  "elapsedSeconds": 42,
  "version": 1,
  "runId": "<uuid|null>",
  "warnings": [ { "code": "<string>", "message": "<string>" } ],
  "nextCheckSeconds": 10,
  "outcome": "Converged|DeliveredWithOpenPoints|Failed|Aborted|null",
  "understanding": "<string|null>",
  "assumptions": [ { "id": "a1", "text": "<string>", "unsure": false } ],
  "questions": [ { "id": "q1", "topic": "audience", "text": "<string>", "options": [ { "id": "o1", "label": "<string>" } ], "defaultOptionId": null, "allowFreeText": false } ],
  "language": "<ISO 639-1|null>",
  "message": "<string|null>",
  "revisionFile": "<string|null>"
}
```

`understanding`, `assumptions`, `questions`, `language`, `message` und `revisionFile` sind nur bei `needs_input` gesetzt (gleiche Form wie bei `submit_order`), sonst `null`. `nextCheckSeconds` ist `0`, wenn `done` wahr ist oder der Zustand `needs_input` lautet, sonst `10`. `outcome` ist das Ergebnis des aktuellen Laufs, sobald er beendet ist.

**Eingabe `get_order_result`:** `{ "orderId": "<uuid>" }`

**Ausgabe `get_order_result`:**
```json
{
  "orderId": "<uuid>",
  "state": "delivered_with_open_points",
  "text": "<string|null>",
  "wordCount": 180,
  "version": 1,
  "placeholders": ["[Liefertermin]"],
  "openChecks": ["<Klartext: eine Prüfung, die der Text nicht bestanden hat>"],
  "sourcesUsed": [ { "fileName": "angebot.pdf", "mode": "full_text|full_text_with_map|excluded", "characters": 160485 } ],
  "warnings": [ { "code": "<string>", "message": "<string>" } ],
  "note": "<string|null>",
  "assumptions": [ { "id": "a1", "text": "<string>", "unsure": true } ]
}
```

`text` ist auch bei `delivered_with_open_points` gesetzt (der beste Text mit den offenen Prüfungen) und bei einem gestoppten Auftrag, der schon einen Text hatte; `null` ist er, solange der Auftrag läuft oder wenn kein Text entstand (`failed`). `note` sagt in Klartext, was dem Menschen zu sagen ist. `assumptions` sind die vor dem Schreiben angezeigten Annahmen, die niemand korrigiert hat; der Schreiber bekam sie als unverbindliche Vorgaben (`null` bei Aufträgen ohne Klärung). Sie gehören zusammen mit dem Text an den Menschen.

**Eingabe `answer_order`:**
```json
{
  "orderId": "<uuid, Pflicht>",
  "answers": [ { "questionId": "length", "origin": "user|client", "optionId": "short", "freeText": "<string|null, höchstens Intake:MaxFreeTextChars (Standard 500) Zeichen>", "decideForMe": false } ],
  "skip": false
}
```

`answers` ist eine typisierte Liste (kein JSON-String). `questionId` und `origin` sind in jeder Antwort Pflicht. `questionId` nennt eine Frage (`q1`…`q3`, `length`), eine Annahme (`a1`, …; dann nur `freeText`, die Korrektur) oder `revision` (nur wenn `revisionFile` gesetzt ist; `optionId` `yes` oder `no`). `origin` ist `user`, wenn der Mensch geantwortet hat, `client`, wenn der Client die Antwort aus dem Gespräch genommen hat; `memory` und andere Werte werden abgelehnt. Eine Frage wird mit `optionId`, `freeText` (nur bei `allowFreeText`) oder `decideForMe` beantwortet – `decideForMe` zusammen mit Option oder Text sowie eine Antwort ohne alles werden abgelehnt. Fragen ohne Antwort gelten als „entscheide du“. `skip = true` oder ein Aufruf ganz ohne Antwort startet sofort mit den Annahmen von Atelier und gilt als übersprungen (mit `skip` auch, solange der Auftrag noch `preparing` ist); `skip` zusammen mit nicht leerem `answers` ist ein Fehler. Unbekannte Fragen oder Optionen und zu lange Texte lehnt der Dienst mit Klartext ab.

**Ausgabe `answer_order`:**
```json
{ "orderId": "<uuid>", "state": "queued|working|…", "runId": "<uuid|null>", "message": "Writing has started. Call get_order_status with waitSeconds, then get_order_result when done is true." }
```

Wartet der Auftrag nicht mehr (schon gestartet, gestoppt, fertig), ändert sich nichts: Die Antwort hat den aktuellen Zustand und den Hinweis des Dienstes in `message` („Your answers were not used.“, wenn der Aufruf Antworten enthielt), ein wiederholter Aufruf schadet also nicht.

Alle Antworten wachsen nur um angehängte Felder; Clients ignorieren Felder, die sie nicht kennen.

#### `submit_request` (Hülle über Aufträge)

Der ältere Einstieg behält seine Antwort, legt aber jetzt einen Auftrag ohne Rückfragen an.

**Eingabe:** `briefingText` (Pflicht), `configJson` (optional; wird nur auf gültiges JSON geprüft und ändert nichts mehr), `crewTemplate` (optional), `customCrew` (optional, CrewSpec-JSON mit mindestens `executorProfileName`), `attachments` (wie bei `submit_order`).

**Ausgabe:**
```json
{ "runId": "<uuid>", "status": "Pending", "errorMessage": null, "orderId": "<uuid>" }
```

Ohne `crewTemplate` und `customCrew` gilt `Orders:LegacySubmitDefaultMode` (Standard: Direktweg; `Careful` schaltet auf das klassische Template zurück). Mit einem von beiden ist der Auftrag `Careful` und nutzt dieses Template bzw. diese Crew. Ungültiges `customCrew`-JSON ist ein Fehler (vor Etappe 1 wurde es still ignoriert). `get_run_status`/`get_run_result` funktionieren weiter mit `runId`; `orderId` gilt für die Auftrags-Werkzeuge. `submit_auto_crew_request` verhält sich wie bisher; auch seine Läufe hängen an Aufträgen.

#### Server-Hinweise und Werkzeugfehler

Beim `initialize` sendet der Server Hinweise: den Auftrag im Wortlaut des Menschen übergeben, Dateien nur als Anhang mit Originalinhalt (nie Platzhalter), dem Menschen die Fragen von `needs_input` mit ihren Antwortmöglichkeiten stellen (selbst nur antworten, wenn es aus dem Gespräch sicher ist, mit `origin = client`; ohne Menschen `answer_order` mit `skip=true`), Aufträge dauern typisch 1 bis 10 Minuten, mit `get_order_status` und `waitSeconds` warten statt mit eigener Sleep-Schleife, den Text unverändert samt Platzhaltern, Annahmen und offenen Prüfungen weitergeben, nichts erfinden; `submit_request` stellt nie Rückfragen.

Lehnt ein Werkzeug einen Aufruf ab, antwortet es mit `isError: true` und dem Text `An error occurred invoking '<tool>': <Meldung>`; die Meldung ist englischer Klartext und sagt, wie der Aufruf zu korrigieren ist.

#### `list_run_artifacts`

Listet alle Artefakte auf, die ein Run erzeugt hat.

**Eingabe:**
```json
{ "run_id": "<uuid>" }
```

**Ausgabe:** Array von Artefakt-Objekten:
```json
[
  {
    "artifact_id": "<uuid>",
    "finalizer_profile_name": "<string>",
    "artifact_type": "File|Url|Status",
    "filename": "<string|null>",
    "content_type": "<string|null>",
    "size_bytes": 12345,
    "storage_uri": "<string>",
    "status_message": "<string|null>",
    "created_at": "<ISO8601>"
  }
]
```

Gibt ein leeres Array zurück, wenn der Run keine Artefakte hat. Auth: Owner-Check (gleiche Run-Isolation wie andere Run-Tools).

---

#### `list_grounding_provider_profiles`

Listet alle Grounding-Provider-Profile (System + Custom) auf.

**Eingabe:** `{ "includeSystem": bool (default true) }`

**Ausgabe:** Array von Grounding-Provider-Profil-Objekten:
```json
[
  {
    "name": "tavily-refined",
    "displayName": "Tavily Refined",
    "description": "...",
    "providerType": "tavily",
    "maxQueriesPerRun": 3,
    "isSystem": true,
    "refinementEnabled": true,
    "refinementMode": "filter"
  },
  {
    "name": "tavily-news",
    "displayName": "Tavily News",
    "description": "...",
    "providerType": "news-search",
    "maxQueriesPerRun": 1,
    "isSystem": true,
    "refinementEnabled": true,
    "refinementMode": "filter"
  },
  {
    "name": "custom-mein-provider",
    "displayName": "Mein Provider",
    "description": "...",
    "providerType": "vector-store",
    "maxQueriesPerRun": null,
    "isSystem": false,
    "refinementEnabled": false,
    "refinementMode": null
  }
]
```

| Feld | Typ | Beschreibung |
|---|---|---|
| `name` | string | Profilbezeichner |
| `displayName` | string | Anzeigename |
| `description` | string | Zweckbeschreibung |
| `providerType` | string | `"tavily"`, `"vector-store"`, `"static-context"`, `"url-fetch"`, `"news-search"`, `"academic-search"`, `"rest-api"` oder `"learning-retrieval"` |
| `maxQueriesPerRun` | int? | Max. Anfragen pro Run (null = unbegrenzt) |
| `isSystem` | boolean | Eingebautes System-Profil |
| `refinementEnabled` | boolean | Ob ein KI-Refinement konfiguriert ist |
| `refinementMode` | string \| null | `"filter"` oder `"synthesize"` (null wenn kein Refinement) |

---

#### `download_run_artifact`

Lädt den binären Inhalt eines `File`-Artefakts herunter und gibt ihn als Base64 zurück.

**Eingabe:**
```json
{ "run_id": "<uuid>", "artifact_id": "<uuid>" }
```

**Ausgabe:**
```json
{
  "artifact_id": "<uuid>",
  "filename": "<string>",
  "content_type": "<string>",
  "size_bytes": 12345,
  "content_base64": "<base64-kodierter Dateiinhalt>"
}
```

Funktioniert nur für `ArtifactType.File`-Artefakte. Liest die Datei vom `ExportPath` auf der Festplatte und gibt die rohen Bytes als Base64 zurück. Auth: Owner-Check.

---

#### `list_learnings`

**Nur für Administratoren** (seit Etappe 1): andere Nutzer bekommen den Fehler „Only administrators can list learning entries, because they contain material from every user.“ Listet Learning-Einträge aller Nutzer aus dem Learning-Store (D-054). Seit D-067 ist die Learning-Schleife standardmäßig pausiert (`Learning:Enabled = false`); das Tool listet weiterhin die vorhandenen Einträge, solange der Schalter aus ist, kommen aber keine neuen hinzu.

**Input:**
```json
{
  "status_filter": "Proposed|Approved|Rejected",
  "domain_filter": "<string>"
}
```

Beide Parameter sind optional. Ohne `status_filter` werden alle Status zurückgegeben; ohne `domain_filter` alle Domänen.

**Output:** Array von Learning-Entry-Objekten:
```json
[
  {
    "id": "<uuid>",
    "text": "<string, auf 300 Zeichen gekürzt>",
    "domain": "<string>",
    "status": "Proposed|Approved|Rejected",
    "source_run_id": "<uuid>",
    "learning_run_id": "<uuid|null>",
    "owner_username": "<string>",
    "created_at": "<ISO8601>",
    "approved_at": "<ISO8601|null>"
  }
]
```

| Feld | Typ | Beschreibung |
|---|---|---|
| `id` | uuid | Learning-Entry-Bezeichner |
| `text` | string | Der kondensierte Lerntext, auf 300 Zeichen gekürzt |
| `domain` | string | Template-Name des Quell-Runs (für Domänen-Boost-Retrieval) |
| `status` | string | `"Proposed"`, `"Approved"` oder `"Rejected"` |
| `source_run_id` | uuid | Der Standard-Run, der die Extraktion ausgelöst hat |
| `learning_run_id` | uuid \| null | Der Learning-Evaluation-Run, der den Eintrag verarbeitet hat (null solange Proposed) |
| `owner_username` | string | Geerbt vom erstellenden Nutzer des Quell-Runs |
| `created_at` | ISO8601 | Extraktions-Zeitstempel |
| `approved_at` | ISO8601 \| null | Genehmigungs-Zeitstempel (null solange Proposed oder Rejected) |

**Hinweis:** `structured_facts_json` wird bewusst nicht exponiert — das rohe Fakten-JSON ist für MCP-Kontext zu umfangreich und nur über die Web-UI zugänglich. Auth: nur Administratoren, weil die Liste Einträge aller Nutzer enthält.

#### Lauf-Status und Ergebnis: `warnings` und `outcome`

Seit D-067 liefern `get_run_status` und `get_run_result` zwei zusätzliche Felder. Beide sind additiv;
ältere Clients können sie ignorieren. Feldnamen sind camelCase.

**Ausgabe von `get_run_status`:**
```json
{
  "runId": "<uuid>",
  "status": "Pending|Running|Completed|Failed|Aborted",
  "errorMessage": "<string|null>",
  "warnings": [ { "code": "<string>", "message": "<string>" } ],
  "outcome": "Converged|DeliveredWithOpenPoints|Failed|Aborted|null"
}
```

`get_run_result` liefert dieselben Felder und zusätzlich `finalText`.

| Feld | Typ | Beschreibung |
|---|---|---|
| `warnings` | array | Probleme, die der Lauf umgangen hat, statt anzuhalten (übersprungene Quellen, ausgefallene Reviewer, Modell-Failover, Finalizer-Probleme). Leer, wenn es keine gab. `code` ist stabil und maschinenlesbar (z. B. `grounding.provider_skipped`); `message` ist ein kurzer, einfacher englischer Satz, dessen Wortlaut sich ändern kann. |
| `outcome` | string \| null | Was der Lauf geliefert hat, sobald er fertig **und abgelegt ist, inklusive aller Finalizer**. `null`, solange der Lauf noch arbeitet, **auch wenn `status` schon terminal aussieht** (der Status wird vor den Finalizern gesetzt), und bei Läufen, die vor Step47 endeten. |

**Ergebnis lesen:** `status` behält seine Bedeutung. Ein Lauf, der ohne Konvergenz stoppt, hat weiterhin
`status = "Failed"`, liefert aber seinen besten Entwurf mit `outcome = "DeliveredWithOpenPoints"` und
einem `finalText`; dasselbe Ergebnis mit `status = "Aborted"` heißt, dass ein kritischer Reviewer-Befund
einen Lauf gestoppt hat, der schon einen Text hatte. Ein Client wartet, bis `outcome` gesetzt ist (nicht nur
auf einen terminalen `status`), und liest erst dann `finalText`; vorher kann ein Finalizer den Text noch
ändern.

`outcome = "Failed"` oder `"Aborted"` heißt nicht, dass es keinen Text gibt: Ein Lauf, der nach dem
Speichern seines Textes abgebrochen wurde, behält ihn (ein während der Finalizer gestoppter Lauf kann sogar
`status = "Completed"` mit `outcome = "Aborted"` behalten), und ein Lauf, dessen Ergebnis nicht vollständig
gespeichert werden konnte, ist `Failed` mit der Warnung `run.result_not_saved`. `finalText` im Ergebnis
prüfen; bei `Failed` ist er nicht sicher vollständig. Einzelheiten:
[02-architecture](02-architecture_de.md#lauf-ergebnis-outcome-und-runstatus).

---

## OAuth-Endpunkte

### Discovery

#### `GET /.well-known/oauth-authorization-server`

**Auth:** Keine  
**RFC:** 8414

Gibt die Server-Metadaten als JSON zurück. Clients nutzen diesen Endpunkt zur automatischen Discovery aller anderen OAuth-Endpunkte.

```json
{
  "issuer": "https://geef.stefan-bechtel.de",
  "authorization_endpoint": "https://geef.stefan-bechtel.de/oauth/authorize",
  "token_endpoint": "https://geef.stefan-bechtel.de/oauth/token",
  "registration_endpoint": "https://geef.stefan-bechtel.de/oauth/register",
  "revocation_endpoint": "https://geef.stefan-bechtel.de/oauth/revoke",
  "response_types_supported": ["code"],
  "grant_types_supported": ["authorization_code", "refresh_token"],
  "code_challenge_methods_supported": ["S256"],
  "token_endpoint_auth_methods_supported": ["none"],
  "scopes_supported": ["mcp:full"]
}
```

---

#### `GET /.well-known/oauth-protected-resource`

**Auth:** Keine  
**RFC:** Draft (MCP Resource Metadata)

Gibt Metadaten zur geschützten Ressource (dem MCP-Server) zurück.

```json
{
  "resource": "https://geef.stefan-bechtel.de/mcp",
  "authorization_servers": ["https://geef.stefan-bechtel.de"],
  "bearer_methods_supported": ["header"],
  "scopes_supported": ["mcp:full"]
}
```

---

### Client-Registrierung

#### `POST /oauth/register`

**Auth:** Keine (oder optionaler `Authorization: Bearer <REGISTRATION_TOKEN>` wenn konfiguriert)  
**RFC:** 7591 — Dynamic Client Registration

Registriert einen neuen OAuth-Client. Typischerweise vom MCP-Client automatisch aufgerufen.

**Request (JSON):**
```json
{
  "client_name": "Mein Client",
  "redirect_uris": ["https://example.com/callback"],
  "client_id": "mein-client-id",
  "logo_uri": null,
  "client_uri": null
}
```

`client_id` ist optional — wird weggelassen, generiert der Server eine UUID. `client_name` und `redirect_uris` sind Pflichtfelder.

**Response (201):**
```json
{
  "client_id": "mein-client-id",
  "client_id_issued_at": 1747390000,
  "redirect_uris": ["https://example.com/callback"],
  "client_name": "Mein Client",
  "token_endpoint_auth_method": "none",
  "grant_types": ["authorization_code", "refresh_token"],
  "response_types": ["code"]
}
```

---

### Authorization-Code-Flow

#### `GET /oauth/authorize`

**Auth:** Cookie (Geef.Atelier-Login — wird bei fehlender Session zu `/login` weitergeleitet)

Startet den Authorization-Code-Flow. Zeigt dem eingeloggten Nutzer die Consent-Seite mit Client-Name und beantragter Berechtigung.

**Pflicht-Query-Parameter:**

| Parameter | Beschreibung |
|-----------|-------------|
| `response_type` | Muss `code` sein |
| `client_id` | Registrierte Client-ID |
| `redirect_uri` | Muss exakt mit einer registrierten URI übereinstimmen |
| `code_challenge` | PKCE-Challenge (Base64Url-codierter SHA-256-Hash des Verifiers) |
| `code_challenge_method` | Muss `S256` sein (`plain` wird abgelehnt) |

**Optionale Parameter:** `scope`, `state`

Beispiel-URL wie sie Claude Desktop aufruft:
```
https://geef.stefan-bechtel.de/oauth/authorize
  ?response_type=code
  &client_id=claude-ai
  &redirect_uri=https%3A%2F%2Fclaude.ai%2Fapi%2Fmcp%2Fauth_callback
  &code_challenge=Oaa_K782ehJ6ZNf-INVXFk1mEKtzQz7xOERSXZUiGXA
  &code_challenge_method=S256
  &state=<zufälliger-state>
  &scope=mcp%3Afull
```

Die `GET /oauth/authorize`-Seite ist eine server-gerenderte Blazor-Consent-Seite
(`[Authorize]` Cookie). Die Approve/Deny-Entscheidung wird per Formular-POST an
`/oauth/consent` gesendet (siehe unten) — nicht an `/oauth/authorize` selbst.

---

#### `POST /oauth/consent`

**Auth:** Cookie (Geef.Atelier-Login) + Anti-Forgery-Token  
**Content-Type:** `application/x-www-form-urlencoded`

Submit-Ziel der Consent-Seite. Verarbeitet die Zustimmung/Ablehnung des Nutzers,
erzeugt bei Zustimmung den Authorization-Code und führt den Redirect aus.

**Nach Zustimmung:** Redirect zu `redirect_uri?code=<auth_code>&state=<state>`  
**Nach Ablehnung:** Redirect zu `redirect_uri?error=access_denied&state=<state>`  
**Bei ungültigem Request:** Fehlerseite (kein Redirect — schützt vor Open-Redirect)

---

### Token-Endpunkt

#### `POST /oauth/token`

**Auth:** Keine (Public Clients — Authentifizierung über PKCE statt Client-Secret)  
**Content-Type:** `application/x-www-form-urlencoded`

Tauscht einen Authorization-Code gegen Tokens oder erneuert über Refresh-Token.

**Grant: `authorization_code`**

```
grant_type=authorization_code
&code=<auth_code>
&client_id=<client_id>
&redirect_uri=<redirect_uri>
&code_verifier=<pkce_verifier>
```

**Grant: `refresh_token`**

```
grant_type=refresh_token
&refresh_token=<refresh_token>
&client_id=<client_id>
```

**Response (200):**
```json
{
  "access_token": "<opaque_token>",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "<opaque_token>",
  "scope": "mcp:full"
}
```

Response-Header: `Cache-Control: no-store`, `Pragma: no-cache` (RFC 6749 §5.1).

**Fehlerverhalten:**
- Ungültiger oder verbrauchter Code → `400 invalid_grant`
- Refresh-Token bereits benutzt → `400 invalid_grant` **+ sofortige Revocation aller Tokens des Nutzers** (Diebstahl-Erkennung nach RFC 6819)

---

### Revocation

#### `POST /oauth/revoke`

**Auth:** Keine  
**Content-Type:** `application/x-www-form-urlencoded`  
**RFC:** 7009

Widerruft einen Access-Token oder Refresh-Token. Gibt immer `200 OK` zurück (auch wenn das Token unbekannt ist).

```
token=<token>
&client_id=<client_id>
```

---

## Web-UI- & Account-Endpunkte

Die Web-Oberfläche ist Blazor Server (Cookie-Auth). Auswahl der relevanten,
extern erreichbaren Routen:

| Endpunkt | Methode | Auth | Zweck |
|----------|---------|------|-------|
| `/` | GET | Cookie | Startseite / Atelier-Übersicht |
| `/health` | GET | Keine | Health-Check (`Healthy`) — für Reverse-Proxy/Container-Lifecycle; umfasst die Datenbank und den Check `pending-migrations` (D-067) |
| `/login` | GET/POST | Keine | Login-Seite (Static SSR) |
| `/auth/logout` | POST | Cookie | Logout (Anti-Forgery, Minimal-API) |
| `/settings/theme` | POST | Keine | Theme-Wechsel-Fallback (No-JS), Redirect zum Referer |
| `/hubs/runs` | WS | Cookie | SignalR-Hub für Live-Run-Updates, nur für Angemeldete; Gruppennamen leitet der Server ab, und die Verbindung schließt bei Ablauf der Anmeldung (D-067). Die eigenen Seiten der Anwendung nutzen stattdessen den prozessinternen `IRunUpdateFeed`; ein externer Client ist nicht bekannt, der Hub entfällt in Etappe 8 (D-068). |
| `/access-denied` | GET | Cookie | Erscheint für Angemeldete, die eine Seite ohne Berechtigung öffnen (D-067) |
| `/admin/users` | GET | Cookie (Admin) | Benutzerverwaltung |
| `/admin/oauth-clients` | GET | Cookie (Admin) | OAuth-Client-Verwaltung |
| `/account/connected-clients` | GET | Cookie | Selbstverwaltung der eigenen verbundenen OAuth-Clients |
| `/crew`, `/crew/templates`, `/crew/profiles/*`, `/crew/studio`, `/crew/knowledge` | GET | Cookie | Crew-/Template-/Profil-/Studio-/Wissensbasis-Verwaltung |
| `/start` | GET | Cookie | Startseite für einen neuen Auftrag: Anfrage, Dateien mit Quittung, „Extra careful“ (D-070) |
| `/work`, `/orders` | GET | Cookie | „My work“: die Aufträge der Person, neueste Aktivität zuerst (Admins können alle Nutzer zeigen) |
| `/orders/{id}` | GET | Cookie | Auftragsseite: Fortschritt, Text, offene Punkte, „How it was made“, Versionen |
| `/runs`, `/runs/{id}`, `/new` | GET | Cookie | Run-Liste, Run-Detail, neuer Auftrag |
| `/crew/providers/*`, `/tools/*`, `/mcp-servers/*`, `/crew/profiles/grounding-providers/create`, `/crew/profiles/grounding-providers/edit/{name}` | GET | Cookie (Admin) | Provider-, Tool-, MCP-Server- und Grounding-Editoren — seit D-067 nur für Admins, weil sie Geheimnisse beim Namen nennen |

Run-bezogene Seiten unterliegen der Run-User-Isolation (D-042): jeder Nutzer sieht
nur eigene Runs; der Admin kann per explizitem Umschalter systemweit sehen.

---

## Endpunkte für Quellen und Auftragsexport (D-070)

Alle drei verlangen die Cookie-Anmeldung; API-Aufrufe ohne sie bekommen `401`/`403` statt einer Weiterleitung zur
Anmeldeseite. Schreibende Aufrufe brauchen das Antiforgery-Token im Header `RequestVerificationToken` und, wenn der
Browser einen schickt, einen `Origin` gleich dem Host (sonst `400`).

| Endpunkt | Methode | Antwort |
|---|---|---|
| `/api/sources` | POST (multipart, genau eine Datei) | `201` mit der Quittung und `Location: /api/sources/{id}`; `200` mit `duplicate: true`, wenn dieselbe Person dieselbe Datei schon hochgeladen hat; `422` mit `usable: false`, `sourceId: null` und `reason` bei einer Datei ohne lesbaren Text, mit weit mehr Text als eine Quelle haben darf, über dem Kontingent oder wenn der Dienst ausgelastet ist; `413` mit `{ "reason": "…" }` bei einer Datei über der Größengrenze; `429` mit der Quittung und `Retry-After: 5`, wenn die Person schon zwei Uploads laufen hat; `408` mit `{ "reason": "…" }`, wenn der Upload länger als `Sources:UploadTimeout` dauert; `400` mit `{ "reason": "…" }` bei fehlendem Token, kaputtem oder abgebrochenem Body oder nicht genau einer Datei. Der Body wird als Strom gelesen; das Token muss im Header kommen |
| `/api/sources/{id}` | GET | Die Quittung einer eigenen Quelle, sonst `404` |
| `/api/sources/{id}` | DELETE | `204`; `409` mit Begründung, solange ein Auftrag die Datei nutzt; `404` für unbekannte oder fremde Quellen |
| `/orders/{id}/export/{format}` | GET | Der Text des Auftrags als `docx`, `pdf` oder `md` (optional `?version=n`); `404` für unbekannte oder fremde Aufträge oder einen Auftrag ohne Text, `400` für ein unbekanntes Format |

Die Quittung ist JSON in camelCase mit Enum-Werten als camelCase-Strings, zum Beispiel:

```json
{
  "sourceId": "<uuid|null>", "fileName": "offer.pdf", "kind": "pdf", "sizeBytes": 2202009,
  "pageCount": 80, "charCount": 160485, "wordCount": 24100, "quality": "good|partial|poor|unreadable",
  "usable": true, "duplicate": false, "budgetClass": "fullText|withMap|tooLarge",
  "preview": "<Anfang des Textes>", "warnings": [ { "code": "pdf.pages_without_text", "message": "…", "pages": [3, 7] } ],
  "reason": null, "estimatedTokens": 84466
}
```

Aufträge selbst startet die Startseite über `IOrderService` im Blazor-Circuit; einen eigenen HTTP-Endpunkt zum
Anlegen eines Auftrags gibt es nicht.

---

## Artefakt-Download-Endpunkt

### `GET /runs/{runId:guid}/artifacts/{artifactId:guid}/download`

**Auth:** `.RequireAuthorization()` — erfordert eine aktive Session (Cookie oder Bearer-Token)

Lädt eine Artefakt-Datei herunter, die ein Finalizer während eines Runs erzeugt hat. Nur Artefakte vom Typ `File` können heruntergeladen werden; `Url`- und `Status`-Artefakte liefern 404.

**Owner-Check:** Nicht-Admin-Nutzer dürfen nur Artefakte aus eigenen Runs herunterladen. Die Prüfung erfolgt über `IRunService.GetRunAsync` mit dem anfragenden Benutzernamen. Admin-Nutzer umgehen den Owner-Check.

**Sicherheitshinweis:** Ein Path-Containment-Guard (`Path.GetFullPath`-Vergleich) verhindert Directory-Traversal-Angriffe auf den serverseitigen Dateipfad.

**Erfolgsantwort:** `200 OK` — Datei-Stream mit `Content-Disposition: attachment` (Dateiname aus dem Artefakt-Datensatz).

**Fehlerantworten:**

| Status | Bedingung |
|--------|-----------|
| `401 Unauthorized` | Nicht authentifiziert |
| `403 Forbidden` | Authentifiziert, aber nicht der Run-Eigentümer (und kein Admin) |
| `404 Not Found` | `runId` oder `artifactId` nicht gefunden |
| `404 Not Found` | Artefakt vorhanden, aber vom Typ `Url` oder `Status` (nicht `File`) |
| `404 Not Found` | Datei nicht auf der Festplatte gefunden |

---

## Token-Design

| Eigenschaft | Wert |
|-------------|------|
| Format | Opaque — 32-Byte Zufallsdaten, Base64Url-codiert |
| Speicherung | Nur SHA-256-Hash in der Datenbank |
| Generierung | `RandomNumberGenerator.GetBytes(32)` |
| Vergleich | `CryptographicOperations.FixedTimeEquals` |
| Access-Token-Lebensdauer | 1 Stunde |
| Refresh-Token-Lebensdauer | 30 Tage, Rotation bei jedem Refresh |
| Scope | Nur `mcp:full` (Vollzugriff auf den MCP-Server) |

---

## Vollständiger Flow (Zusammenfassung)

```
Client                          Geef.Atelier                    Browser/Nutzer
  │                                 │                                 │
  │── GET /.well-known/oauth-... ──>│                                 │
  │<── Server-Metadaten ────────────│                                 │
  │                                 │                                 │
  │── POST /oauth/register ────────>│                                 │
  │<── client_id ───────────────────│                                 │
  │                                 │                                 │
  │── Öffne Browser mit GET ────────────────────────────────────────>│
  │   /oauth/authorize?...          │                                 │
  │                                 │<── Login (falls nötig) ─────────│
  │                                 │<── Consent "Zugriff gewähren" ──│
  │                                 │── Redirect ?code=... ──────────>│
  │<── code (via redirect_uri) ─────────────────────────────────────│
  │                                 │                                 │
  │── POST /oauth/token ───────────>│                                 │
  │   (code + code_verifier)        │                                 │
  │<── access_token + refresh_token─│                                 │
  │                                 │                                 │
  │── POST /mcp ───────────────────>│                                 │
  │   Authorization: Bearer <token> │                                 │
  │<── Tool-Response ───────────────│                                 │
```
