# Atelier CLI Proxy

*[English](README.md) · **Deutsch***

Ein leichtgewichtiger HTTP-Wrapper, der die `claude` (Claude Code CLI) und `codex` (OpenAI Codex CLI) in eine OpenAI-kompatible REST API übersetzt. Ermöglicht Geef.Atelier, Subscription-basierte CLI-Kapazitäten zu nutzen, ohne per Token abzurechnen.

## Architektur

```
Geef.Atelier Web ──► POST /v1/claude/chat/completions ──► CLI-Proxy ──► claude CLI
                 ──► POST /v1/codex/chat/completions  ──► CLI-Proxy ──► codex CLI
```

Der Proxy hängt am internen `geef-atelier-network` UND am gemeinsamen Docker-Netz `proxy` — damit kann **jede** App ihn als OpenAI-kompatibles Gateway unter `http://cli-proxy:8090` nutzen. Kein Traefik-Routing, nicht öffentlich exponiert.

## Nutzung aus anderen Apps

Jeder Container im gemeinsamen `proxy`-Docker-Netz kann diesen Proxy als Drop-in-OpenAI-Endpoint ansprechen — Abo-gedeckt, keine Pro-Token-Abrechnung. Ein OpenAI-SDK einfach auf `http://cli-proxy:8090/v1/claude` oder `http://cli-proxy:8090/v1/codex` zeigen lassen (beliebiger `api_key`, sofern `CLI_PROXY_API_KEYS` nicht gesetzt ist):

```python
from openai import OpenAI
client = OpenAI(base_url="http://cli-proxy:8090/v1/codex", api_key="unused")
print(client.chat.completions.create(
    model="gpt-sol-latest", messages=[{"role": "user", "content": "ping"}]).choices[0].message.content)
```

Hängt eine App noch nicht am `proxy`-Netz, ergänzen:

```yaml
services:
  my-app:
    networks: [proxy]
networks:
  proxy:
    external: true
```

## Endpoints

| Methode | Pfad | Beschreibung |
|---------|------|--------------|
| `POST` | `/v1/claude/chat/completions` | Direkt zur claude CLI — kein Model-Name-Routing |
| `POST` | `/v1/codex/chat/completions`  | Direkt zur codex CLI — kein Model-Name-Routing |
| `POST` | `/v1/codex/images/generations` | Bilderzeugung (OpenAI Images API, nur codex, `b64_json`/PNG, ein Bild je Anfrage) |
| `POST` | `/v1/chat/completions` | **DEPRECATED** Legacy-Endpoint mit Model-Name-Routing. Loggt WARNING. |
| `GET`  | `/v1/claude/models` | Modellliste der claude CLI (Always-latest-Aliase plus die konkreten IDs, zu denen die CLI sie auflöst; bei mehreren Claude-Konten die Vereinigung aller) |
| `GET`  | `/v1/codex/models` | Abo-Modell-Liste der codex CLI aus `$CODEX_HOME/models_cache.json` (nur `visibility: list`, nach `priority` sortiert, nie `-pro`/`:batch`), davor die Always-latest-Aliase `gpt-<Linie>-latest` (`x_aliases` bildet jeden auf die konkrete ID ab, `x_aliases_complete: true`); kuratierter Fallback `gpt-6.2-sol`, `gpt-6.2-astra`, `gpt-6.1-terra`, solange die CLI den Cache noch nicht geschrieben hat (`x_source: static`) |
| `GET`  | `/v1/models` | **DEPRECATED** kombinierte Legacy-Modell-Liste |
| `GET`  | `/health` | Health-Check mit CLI-Status und Failover-Zustand (`degraded`, Breaker je Backend, `last_error_detail`, `empty_results`, `empty_result_alert`, `caller_timeouts`) |

### Explizite Endpoints (empfohlen)

Die neuen Endpunkte `/v1/claude/chat/completions` und `/v1/codex/chat/completions` routen deterministisch anhand des Pfades — unabhängig vom `model`-Feld im Request. Dies ist die empfohlene Variante für alle neuen Profile.

### Legacy-Endpoint (veraltet)

`/v1/chat/completions` bleibt für Backward-Kompatibilität erhalten und nutzt weiterhin Model-Name-Routing:

- `claude-*`, `anthropic/claude-*` → claude CLI
- `gpt-*`, `o1-*`, `o3-*`, `o4-*`, `openai/*` → codex CLI
- Unbekannte Modelle → claude CLI (Fallback)

Jeder Aufruf dieses Endpunkts loggt ein DEPRECATED-Warning. Geplante Entfernung nach 2 Atelier-Versionen.

## OpenAI-Kompatibilität

Der Proxy zielt auf praktische Drop-in-Kompatibilität mit der OpenAI-Chat-Completions-API. Was unterstützt wird — und was die CLI-Backends prinzipiell nicht können:

| Feature | Status |
|---------|--------|
| **Streaming** (`stream: true`) | ✅ SSE-`chat.completion.chunk`-Stream, abschließendes `data: [DONE]`. `stream_options.include_usage` ergänzt einen finalen Usage-Chunk. claude streamt Token-Deltas; codex liefert die Agent-Nachricht als (gepufferten) Delta. Anfragen mit `tools`/`response_format` puffern den vollen Text und emittieren ihn dann. |
| **Echte Token-Usage** | ✅ `usage` wird aus dem CLI-Output geparst (claude `--output-format json`, codex `exec --json`), inkl. `prompt_tokens_details.cached_tokens` und `completion_tokens_details.reasoning_tokens`. |
| **Error-Envelope** | ✅ `{"error": {"message","type","param","code"}}` mit korrekten Status-Codes (422→400, 401, 404, 429, 5xx). |
| **`response_format`** | ✅ `json_object` und `json_schema` (serverseitig gegen das Schema validiert, mit einem Retry, sonst `refusal`). Geprüft wird jedes JSON-Objekt der Antwort (Fences, vorangestellte Prosa, verirrte Klammern, wiederholtes Schema); das erste schemakonforme gewinnt. claude wiederholt im **Lean-Modus** (siehe `README.md`, Abschnitt „Lean mode“; Schalter `CLAUDE_STRUCTURED_LEAN_RETRY`, Standard an, und `CLAUDE_LEAN_CHAT`, Standard an). |
| **Tool-Calling** | ✅ `tool_choice` `auto`/`required`/`none`/spezifisch, parallele Tool-Calls, Multi-Turn-Replay (emuliert via Prompt + JSON-Extraktion). |
| **Multimodal** | ⚠️ Best-Effort: http(s)-`image_url`-Parts werden als Referenzen durchgereicht (claude kann sie per WebFetch laden); inline `data:`-Bilder werden mit Warnung verworfen. |
| **Auth** | ✅ Opt-in Bearer (`CLI_PROXY_API_KEYS`); ungesetzt = offen (rückwärtskompatibel). |
| **`logprobs` / `n>1`** | ❌ mit `400` abgelehnt — die CLI liefert keine Token-Wahrscheinlichkeiten, und `n>1` würde den Abo-Verbrauch vervielfachen. |
| **`temperature` / `top_p` / `seed` / Penalties** | ⚠️ angenommen und **ignoriert** (Headless-CLIs bieten keine Sampling-Steuerung). |
| **`reasoning_effort`** | ✅ wird **pro Anfrage und nur wenn gesetzt** umgesetzt: `low`/`medium`/`high` → claude `--effort <stufe>`, codex `-c model_reasoning_effort="<stufe>"` (auch beim Failover-Ziel). Jeder andere Wert (auch `minimal`) → `400`. Die angewandte Stufe steht im Header `x-cli-proxy-reasoning-effort`. Ohne das Feld bleibt der CLI-Aufruf unverändert, es gilt der CLI-Default. |
| **`system_fingerprint`** | weggelassen (kein stabiler Fingerprint verfügbar). |

Modelle: `GET /v1/{claude,codex}/models` (Liste) und `GET /v1/{claude,codex}/models/{id}` (Einzelabruf). Jede Antwort trägt `x-request-id`, `openai-processing-ms` und informative `x-ratelimit-*`-Header. Optionaler Überlastschutz: `CLI_PROXY_MAX_INFLIGHT` setzen, um bei so vielen gleichzeitigen Anfragen `429` zu liefern (Default `0` = aus, Anfragen reihen sich an den Per-CLI-Semaphoren ein).

### Codex-Always-latest-Aliase

Wie `claude-opus-latest` gibt es für codex `gpt-<Linie>-latest`-Aliase, damit Konsumenten nie auf einem veralteten Modell hängen bleiben. Die Linie ist das letzte Namenssegment einer ID der Form `gpt-<Version>-<Linie>` (`sol`, `astra`, `terra`, `luna`, ...); sie wird aus den gelisteten IDs abgeleitet, eine neue Linie bekommt ihren Alias also automatisch. `gpt-sol-latest` löst auf das neueste gelistete `sol`-Modell auf.

- **Neueste** heißt höchste numerische Version (`gpt-6.2` > `gpt-6.1` > `gpt-6` > `gpt-5.6`, als Zahlen verglichen, nicht als Text); bei gleicher Version entscheidet die Listenreihenfolge. `-pro`, `:batch` und versteckte Einträge zählen nicht mit.
- **Quelle:** die Live-Liste aus `models_cache.json`; ohne sie (`x_source: static` oder `degraded`) lösen die Aliase gegen die kuratierte Fallback-Liste auf.
- **Endpunkte:** `GET /v1/codex/models` und `GET /v1/cli/codex-cli/models` führen die Aliase zuerst in `data`, danach die konkreten IDs, und liefern die Abbildung in `x_aliases` (zum Beispiel `{"gpt-sol-latest": "gpt-6.2-sol", "gpt-astra-latest": "gpt-6.2-astra"}`) sowie `"x_aliases_complete": true`. Das Feld sagt: Die Abbildung ist vollständig und deterministisch aus der Modellliste abgeleitet und exakt die, die der Proxy beim Aufruf anwendet; ein Konsument darf seinen Alias-Stand durch sie ersetzen, statt zu mergen. Es gilt bei `x_source` `live`, `static` und `degraded`. Die claude-Endpunkte lassen das Feld weg (fehlt = `false`). Der Failover auf codex nutzt intern `gpt-sol-latest` (`DEFAULT_CODEX_MODEL`). Die HTTP-API verlangt `model` weiterhin; ein Request ohne das Feld wird mit 400 abgelehnt.
- **Anfragen:** Ein Request mit `model: "gpt-sol-latest"` (ein Präfix wie `openai/gpt-sol-latest` wird ignoriert) ruft `codex -m <konkrete ID>` auf, und das `model` der Antwort trägt die konkrete ID. Ein Alias, den kein gelistetes Modell erfüllt, wird nicht ersetzt, sondern verhält sich wie jeder andere unbekannte Modellname.
- **Reproduzierbarkeit:** Der Alias wandert mit jedem Release. Run-Snapshots und Konsumenten, die reproduzierbare Ergebnisse brauchen, speichern die konkrete ID aus `x_aliases`, nicht den Alias.

## Failover, Fehlerdetails und leere Ergebnisse

Die expliziten Endpunkte übergeben eine Anfrage an die Partner-CLI (claude ⇄ codex), wenn die angefragte mit einem Auth-, Limit- oder Transportfehler scheitert; ein Circuit-Breaker hält ein gestörtes Backend eine Weile fern und lässt dann eine Probe durch. Übergebene Antworten tragen `x-cli-proxy-failover: <von>-><nach>`, jede erfolgreiche Antwort `x-cli-proxy-provider`.

- **Failover je Anfrage abschalten:** Request-Header `x-cli-proxy-failover: off` (auch `0` oder `false`, Groß-/Kleinschreibung egal), wenn das Ergebnis vom Modell abhängt und eine Antwort eines anderen Backends schlechter ist als ein Fehler. Die Anfrage bleibt dann beim angefragten Backend (kein zweites Konto als Failover-Stufe, keine Partner-CLI; das unten beschriebene Modell-Routing zwischen den Claude-Konten gilt weiterhin). Ein Fehler kommt als übliche Fehlerhülle mit der Ursache der CLI zurück und zählt für den Breaker wie sonst. Ist der Breaker des angefragten Backends offen, antwortet der Proxy sofort mit `503`, `code: backend_unavailable` und `Retry-After`, statt umzuleiten. Ohne den Header oder mit einem anderen Wert ändert sich nichts.
- **Nur dasselbe Modell, je Anfrage:** Request-Header `x-cli-proxy-failover: same-model` (Groß-/Kleinschreibung egal), wenn das Ergebnis vom Modell abhängt, ein anderes Konto derselben CLI es aber liefern darf, weil es dasselbe Modell unter demselben Namen ausführt. Eine Claude-Anfrage darf dann zwischen den Claude-Konten wechseln (`claude` ⇄ `claude-alt`), mit unverändertem angefragten Modell und aus denselben Gründen wie beim normalen Failover (Auth- und Limitfehler; ein Transportfehler überspringt wie immer das zweite Konto), aber nie zu codex. Eine codex-Anfrage wird genau wie `off` behandelt (sie geht nie zu Claude). Breaker, Limit-Erkennung, „nicht im Abo“-Routing und Antwortköpfe verhalten sich wie beim normalen Failover zwischen den Konten: Eine Übergabe trägt `x-cli-proxy-provider: claude-alt` und `x-cli-proxy-failover: claude->claude-alt`. Konten mit offenem Breaker werden übersprungen; sind alle Konten der CLI gesperrt, lautet die Antwort `503`, `code: backend_unavailable`, mit `Retry-After` bis zum ersten Konto, das wieder eine Probe annimmt. Mit `FAILOVER_ENABLED=0` verhält sich der Wert wie `off`. Es zählt nur genau diese Schreibweise: Jeder andere Wert (auch `same_model`) gilt wie „kein Header“, also volles Failover. Ein Gateway, das den Wert noch nicht kennt, behandelt ihn ebenso; deshalb zuerst das Gateway aktualisieren, dann die Aufrufer.
- **Mehrere Claude-Konten, unterschiedliche Modelle:** Mit einem zweiten Claude-Login können die Konten unterschiedliche Modelle enthalten. Die Alias-Probe läuft für jedes Konto, und die claude-Modelllisten (`/v1/claude/models`, `/v1/cli/<claude-Provider>/models`, `/v1/models`) liefern die Vereinigung: `x_aliases` nimmt je Familie die Auflösung des Hauptkontos und sonst die des zweiten Kontos. Eine Anfrage für ein Modell, das nur das zweite Konto enthält, geht direkt dorthin (`x-cli-proxy-provider: claude-alt`, kein `x-cli-proxy-failover`-Header). Das ist Routing, kein Failover: Es gilt auch mit `x-cli-proxy-failover: off` und bewegt keinen Circuit-Breaker. Antwortet ein Konto, dass sein Abo das Modell nicht enthält, merkt sich der Proxy das eine Zeit lang (`CLAUDE_NOT_IN_PLAN_TTL_SECONDS`, Standard 3600), versucht dasselbe Modell auf dem anderen Claude-Konto und übergibt, wenn keines es hat, bei erlaubtem Failover an codex oder antwortet mit `404`, `code: model_not_found`. Es zählt nur das strukturierte Fehlerergebnis der CLI (`is_error: true` mit passender Meldung, ohne den angefragten Modellnamen), nie freier Text, und ein gewöhnliches Sitzungs- oder Wochenlimit gilt nie als „nicht im Abo“. Höchstens `CLAUDE_MODEL_PROBE_CONCURRENCY` (Standard 2) Proben laufen gleichzeitig, und ein gewöhnlicher Modelllisten-Abruf wartet nie auf Proben des zweiten Kontos (es wird im Hintergrund aufgefrischt). `/health` zeigt unter `claude_accounts` je Konto, welche Familien auflösen und welche nicht im Abo sind.
- **Fehlerdetails:** Endet eine CLI mit Exit-Code ≠ 0, nennt die Fehlermeldung die Ursache statt der ersten 200 Zeichen von stdout: bei claude die Felder `result` (höchstens 300 Zeichen), `subtype`, `is_error` und `api_error_status` des JSON-Ergebnisses, bei codex stderr plus die Meldung des letzten Fehlerereignisses; ohne solche Felder wie bisher die ersten 200 Zeichen der Ausgabe. Geheimnisähnliche Stellen (`sk-…`, `tvly-…`, `AIza…`, `gsk_…`, Bearer-Token, `key=…` u. Ä.) werden durch `[redacted]` ersetzt (`src/redaction.py`, dieselben Regeln wie in Geef.Atelier). Prompt- und Kontexttexte erscheinen nie in diesen Meldungen. In den Streaming-Pfaden wird stderr parallel zu stdout gelesen, sodass eine CLI mit viel Ausgabe auf stderr nie blockiert; aufbewahrt werden nur die letzten 64 KB (wurde etwas verworfen, ab der ersten vollständigen Zeile), und die Meldung eines fehlgeschlagenen Streams zeigt die ersten 200 Zeichen dieses Teils.
- **Leere Ergebnisse im Dokumentmodus:** Ein Dokumentmodus-Lauf (claude oder codex, auch nach einem Failover), der normal endet, `draft.md` aber leer und unverändert lässt, wird mit `502`, `type: api_error`, `code: empty_result` beantwortet statt mit `200` und leerem Text. Er ist nur ein Fehler dieser einen Anfrage und wird nicht erneut übergeben. Für den Circuit-Breaker zählt er **nicht**, weder beim Backend, das ihn geliefert hat, noch nach einem Failover beim Ursprung: Ob die CLI den Entwurf schreibt, hängt vom Inhalt der Anfrage ab (Ablehnung, Chat-Antwort statt Dateischreiben). Das ist kein Beleg für ein krankes Backend, und eine einzelne Anfrage darf ein Backend nicht für alle anderen Aufrufer sperren. Er erhöht `consecutive_failures` nicht und setzt es nicht zurück; er wird nur in `/health` gezählt und gemeldet. In den Entwurf geschriebener Text, ein unveränderter nicht leerer Entwurf sowie normale Chat- und Streaming-Antworten verhalten sich wie bisher.
- **`/health`:** Jeder Eintrag unter `failover.backends` trägt zusätzlich `last_error_detail` (die bereinigte Fehlermeldung mit Ursache, bis 2000 Zeichen; `last_error` behält seinen Schnitt bei 500 Zeichen) und `empty_results` (Zahl leerer Dokumentmodus-Ergebnisse seit dem Start). Für die leeren Ergebnisse kommen `last_empty_result_at` (Epochensekunden), `empty_results_recent` und `document_requests_recent` (leere Ergebnisse und abgeschlossene Dokumentmodus-Versuche dieses Backends in den letzten `failover.empty_result_window_seconds`, Standard 600) sowie `empty_result_rate` (deren Verhältnis, `null` ohne Dokumentmodus-Versuche) hinzu.
- **`empty_result_alert`:** `true` bei einem Backend, wenn es im Fenster mindestens 5 leere Ergebnisse geliefert hat **und** mehr als 50 % seiner Dokumentmodus-Versuche leer zurückkamen; `failover.empty_result_alert` ist `true`, sobald ein Backend das meldet. Nur Anzeige für Wächter und Betreiber: Es ändert kein Routing und ist unabhängig von `degraded`. Schwellen: `EMPTY_RESULT_ALERT_WINDOW_SECONDS` (600), `EMPTY_RESULT_ALERT_MIN` (5), `EMPTY_RESULT_ALERT_RATIO` (0.5).
- **Bekannte Grenze, codex-Dokumentmodus im Container:** codex läuft im Dokumentmodus mit `--sandbox workspace-write`. Dessen Linux-Sandbox (bubblewrap) braucht User-Namespaces, die das Standard-seccomp-Profil von Docker nicht gewährt; codex kann dort weder `context.md` lesen noch `draft.md` schreiben. Solche Läufe enden jetzt als `empty_result` statt als leerer Erfolg.

## Lebensdauer der CLI-Prozesse, Zeitgrenze des Aufrufers und Verbindungsabbruch (seit 2026-11-25)

Jeder CLI-Lauf einer Anfrage startet in einer eigenen Prozessgruppe. Endet ein Lauf vorzeitig, beendet der Proxy die **ganze Gruppe** (die CLI und alles, was sie gestartet hat: Node-Worker, Werkzeug- und MCP-Helfer), nicht nur die CLI. Das gilt für alle Pfade, die für eine Anfrage eine CLI starten: claude und codex, Print- und Lean-Modus, Dokumentmodus, Tool-Use über die Entscheidungsdatei, Streaming und ohne Streaming, codex-Bilder sowie die gemini-/generic-Adapter von `/v1/cli/{provider}`. Die „ganze Gruppe“ gilt auch, wenn die CLI selbst schon beendet ist, einer ihrer Helfer aber noch stdout oder stderr offen hält und der Proxy deshalb noch auf Ausgabe wartet. Dann wird die Gruppe nur signalisiert, solange diese Ausgabe offen ist und kein laufender Prozess die Gruppen-ID als eigene PID trägt (eine neu vergebene Nummer könnte nicht unsere sein). Nicht erreicht werden Prozesse, die die Gruppe verlassen (eigene Session), sowie Helfer ohne offene Ausgabe-Pipe, die einen regulär beendeten Lauf überleben. Die Modelllisten-Proben (`GET …/models`, Aufwärmen) behalten ihre eigenen kurzen Zeitgrenzen.

- **Aufrufer trennt die Verbindung:** Schließt der Aufrufer die Verbindung, bevor die Antwort vollständig ist (eigene Zeitgrenze, abgebrochener Auftrag, Absturz), wird die Anfrage abgebrochen und die Prozessgruppe ihrer CLI beendet. Für diese Anfrage startet danach nichts mehr: keine Übergabe an die Partner-CLI, keine claude-alt-Stufe, keine JSON-Wiederholung. Ein Abbruch zählt nie für den Circuit-Breaker und erscheint nicht in `last_error`; CLI-Plätze (Semaphore), Arbeitsverzeichnisse und der In-flight-Zähler werden freigegeben. Das gilt auch für einen Streaming-Aufrufer, der nicht mehr liest und dann trennt: Die Antwort schließt an ihrem Ende die gesamte Kette der Stream-Generatoren, CLI und Platz werden sofort frei, mit der asyncio-Standardschleife ebenso wie mit uvloop (das Gateway schließt die Pipes einer CLI, die es beendet, daher kann eine durch Rückstau pausierte Pipe den Platz nicht blockieren). Je abgebrochener Anfrage eine Logzeile: `caller disconnected, request aborted | request_id=… | backend=… | after=…s` (ohne Inhalte). Vorher lief ein solcher Lauf bis `CLI_TIMEOUT_SECONDS` (Standard 1800 s) weiter. Die Erkennung folgt der ASGI-Semantik: erst nachdem der vollständige Anfragekörper gelesen ist, und nur ein `http.disconnect` vor dem letzten Antwortteil zählt. Wer verbunden bleibt, merkt nichts, auch bei langen Aufrufen und mit HTTP-Keep-Alive. Ein Client, der seinen Socket halb schließt und trotzdem auf die Antwort wartet (selten), gilt als getrennt; er hätte die Antwort auch vorher nicht erhalten.
- **Zeitgrenze des Aufrufers (optional):** Request-Header `x-cli-proxy-timeout-seconds: <Sekunden>` (positive Zahl; alles andere wird mit `400`, `code: invalid_header` abgelehnt). Er gilt für die Chat-Endpunkte, `/v1/cli/{provider}` und den Bild-Endpunkt (dort greift zusätzlich die eigene Obergrenze eines Bildlaufs, `IMAGE_TIMEOUT_SECONDS`; eine angenommene Bildanfrage zählt auch dann gegen das Tageslimit, wenn ihre Zeit abläuft). Die Grenze zählt ab Eingang der Anfrage (Warten auf einen CLI-Platz und Wiederholungen im Proxy verbrauchen sie; wartet die Anfrage noch auf einen Platz, wird sie beantwortet, sobald die Grenze abläuft, ohne eine CLI zu starten) und ist nach oben durch `CLI_TIMEOUT_SECONDS` begrenzt; ein Wert ab dieser Obergrenze ändert nichts. Läuft sie ab, wird die Prozessgruppe beendet und die Anfrage mit `504`, `type: api_error`, `code: cli_timeout` beantwortet. Das ist die Wahl des Aufrufers, kein Backend-Fehler: Die Anfrage wird **nicht** an die Partner-CLI übergeben und bewegt den Breaker **nicht**. `/health` meldet das je Backend in eigenen Feldern (`caller_timeouts`, `last_caller_timeout_at`, `last_caller_timeout_error`); `last_reason`, `last_error`, `last_error_detail`, `last_failure_at` (und damit die `FAILOVER`-Logzeile) beschreiben weiterhin echte Fehler. Bei Streaming kommt der Fehler im Strom. Den Wert einige Sekunden unter die eigene Client-Zeitgrenze legen, damit der Proxy die CLI beendet, bevor der eigene Client aufgibt. Geef.Atelier sendet ihn beim Faktenprüfer (85 s bei 90 s Grenze, 55 s beim Ersatzmodell mit 60 s).
- **Ohne Header und mit verbundenem Aufrufer:** Antworten, Fehler und Zeitüberschreitungen wie bisher; die eigene Obergrenze (`CLI_TIMEOUT_SECONDS`) behält ihren Fehlertext und ihre Wirkung auf Failover und Breaker, beendet jetzt aber auch die Hilfsprozesse der CLI.
- **Hinweis Container:** uvicorn als PID 1 würde verwaiste Enkelprozesse nicht abräumen. Deshalb setzt der Compose-Dienst `init: true`: Ein Init-Prozess läuft als PID 1 und räumt beendete Helfer ab, es bleiben keine Zombies zurück. Wer das Image anders startet, nimmt ebenfalls einen Init-Prozess (`docker run --init`).

## Tool-Use

Wenn der Request `tools` + `tool_choice` enthält, wird das Schema als Prompt-Addendum eingebettet:

> "Respond ONLY with a valid JSON object conforming to schema ..."

Der CLI-Output wird dann auf einen JSON-Block gescannt (inkl. Markdown-Fence-Stripping) und als `tool_calls`-Response zurückgegeben.

## Websuche (opt-in seit 2026-11-07)

Wie jede OpenAI-kompatible API surft das Gateway nur, wenn der Aufrufer es verlangt. Pro Anfrage schaltet der Standardparameter `web_search_options` (beliebiges Objekt, meist `{}`) die Suche ein; der Betreiber kann den Standard für den ganzen Dienst mit `CLI_WEB_SEARCH=1` beim Service `cli-proxy` umstellen (Verhalten vor dem 2026-11-07). Ohne beides läuft claude mit `--tools ""` und codex ohne `--search`.

Bei eingeschalteter Suche entscheidet das Modell selbst, ob es aktuelle Informationen aus dem Web holt:

- **claude** — Aufruf mit `--allowedTools "WebSearch,WebFetch"`. Nur die Web-Tools sind allowlisted; Bash/Edit/Write sind **nicht** aktiviert (kein voller Permission-Bypass).
- **codex** — Aufruf mit dem globalen `--search`-Flag, platziert **vor** dem `exec`-Subcommand (codex lehnt es als `exec`-Argument ab). Es aktiviert das native Responses-`web_search`-Tool ohne Per-Call-Approval.

**Kosten:** Die Websuche läuft über das Abo (Claude / Codex), nicht pro Suche abgerechnet — kein Tavily-artiges Per-Request-Billing auf dem CLI-Pfad.

**Scope / Einschränkung:** Die vom Modell gesuchten Quellen werden intern verarbeitet und nirgends festgehalten. Geef.Atelier nutzt diesen Weg nicht: Seine Recherche ist ein eigener Schritt mit eigenem Suchschlüssel (D-074).

## Setup (Production)

Der Proxy gehört zum Compose-Stack von Geef.Atelier. Alle Befehle im Wurzelverzeichnis des Repositorys ausführen.

### 1. Bauen und starten

```bash
docker network create proxy   # einmal, falls das gemeinsame Netz noch nicht existiert
docker compose up -d --build cli-proxy
```

### 2. Claude CLI anmelden

```bash
docker compose exec -e HOME=/auth/claude cli-proxy claude auth login
# Den Anweisungen im Browser folgen
```

`HOME` ist wichtig: Der Proxy startet claude mit `HOME=/auth/claude`. Eine Anmeldung ohne diese Angabe landet im Container unter
`/root`, wirkt nicht und geht mit dem nächsten Container verloren.

### 3. Codex CLI anmelden

```bash
docker compose exec cli-proxy codex login --device-auth
```

`CODEX_HOME=/auth/codex` ist im Container gesetzt. Das Auth-Volume `geef-atelier-cli-auth` hält beide Anmeldungen über
Neustarts.

### 4. Optional: ein zweites Claude-Konto

Unter einem eigenen Home anmelden. Der Proxy nutzt es dann als zweite Stufe vor codex und für Modelle, die nur dieses Konto
führt (siehe oben):

```bash
docker compose exec -e HOME=/auth/claude-alt cli-proxy claude auth login
```

### 5. Prüfen

```bash
docker compose exec -e HOME=/auth/claude cli-proxy claude auth status --text
docker compose exec cli-proxy codex login status
docker compose exec cli-proxy curl -s localhost:8090/health
```

In `/health` heißt `cli_status` `ready` nur, dass eine CLI installiert ist, nicht dass sie angemeldet ist; was mit echten
Aufrufen geschah, zeigen `failover` und `claude_accounts`.

## Konfiguration (Geef.Atelier)

Atelier erreicht das Gateway über seine Provider `claude-cli` und `codex-cli`; `appsettings.json` nennt ihre Endpunkte unter
`Llm:ProvidersFallback` (`http://cli-proxy:8090/v1/claude` und `/v1/codex`). Welche Rolle welchen Provider und welches Modell
nutzt, entscheidet die Modellmatrix beim Anlegen eines Laufs (siehe
[`docs/08-crew-system_de.md`](../docs/08-crew-system_de.md#modellmatrix-d-066)). Ist `CLI_PROXY_API_KEYS` gesetzt, bekommt die
Web-App denselben Schlüssel als `CLI_PROXY_API_KEY` (die Compose-Datei reicht ihn an beide).

Das Gateway holt jede Minute die Einstellungen der CLI-Anbieter von der Web-App (`ATELIER_BACKEND_URL`) und sendet dabei
`ATELIER_INTERNAL_API_TOKEN`; ein Wert in `.env` genügt, die Compose-Datei reicht ihn an beide. Ohne passenden Wert
antwortet die Web-App mit 401, und das Gateway schreibt eine Warnung. Die eingebauten Anbieter `claude-cli`, `codex-cli`
und `gemini-cli` laufen dann mit den eigenen Standardwerten des Gateways weiter, samt Modelllisten, bis ein erster
Abgleich gelingt; eigene CLI-Anbieter brauchen den Abgleich.

## Concurrency

Jede CLI hat eine eigene Grenze gleichzeitiger Aufrufe (`asyncio.Semaphore`); weitere Aufrufe warten. Die Compose-Datei setzt:

```
CLAUDE_MAX_CONCURRENT=6
CODEX_MAX_CONCURRENT=2
```

## Sicherheitshinweis

CLI-Auth-Tokens (in `/auth/`) sind niemals in Source-Control, Logs oder Berichten.

## Tests

```bash
cd cli-proxy
pip3 install ".[dev]" --break-system-packages
python3 -m pytest tests/ -v
```
