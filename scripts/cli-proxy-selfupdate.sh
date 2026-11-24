#!/usr/bin/env bash
# Nächtliches Selbst-Update des cli-proxy (claude-, codex- und gemini-CLI im Image) und Host-Abgleich der CLIs.
#
# Warum: Die Modellliste des Gateways kommt von den CLIs. Eine veraltete claude-CLI kennt neue Modelle nicht
# (Beispiel 2026-11-10: CLI 2.1.319 löste den Alias `fable` auf 5.1 auf, 2.1.322 auf 5.2).
#
# Ablauf (Container-Teil):
#   1. Installierte CLI-Versionen im laufenden Container mit `npm view <paket> version` vergleichen. Nichts Neues: weiter mit Schritt 7.
#   1b. Nur aus dem committeten Stand bauen: Meldet `git -C "$PROJECT_DIR" status --porcelain -- cli-proxy` Änderungen, liegen
#      unter den Pfaden, die das Dockerfile per COPY ins Image holt (IMAGE_SOURCE_PATHS), ignorierte Dateien (außer den harmlosen
#      __pycache__/, .pytest_cache/, *.egg-info/), oder ist der Git-Stand nicht prüfbar, wird der Image-Neubau diese Nacht
#      ausgesetzt (Logzeile, Telegram-Hinweis, Schlusszeile „Fertig (Status 0, ausgesetzt)“). Das gilt auch mit FORCE=1. Der
#      Host-Abgleich (Schritt 7) läuft trotzdem. Geprüft wird dreimal: vor der Ruhewartezeit (spart das Warten), unmittelbar vor
#      `docker compose build` (eine Änderung während der Wartezeit fällt hier auf) und noch einmal vor `compose up`: Ist der
#      Stand dann nicht mehr sauber oder zeigt `git rev-parse HEAD:cli-proxy` einen anderen Baum als vor dem Bauen, wird der
#      Container nicht ausgetauscht, $IMAGE:latest auf das laufende Image zurückgetaggt und die Nacht als ausgesetzt beendet
#      (Status 0; scheitert das Zurück-Taggen, Status 2). Die Zielversionen werden dabei nicht gesperrt.
#   2. Ruhe abwarten (keine laufenden claude/codex/gemini-Prozesse im Container, höchstens WAIT_QUIET_SECONDS), sonst diese Nacht
#      den Container-Teil überspringen.
#   3. Vor dem Build die Image-ID des LAUFENDEN Containers erfassen (`docker inspect -f '{{.Image}}'`) und genau diese ID als
#      geef-atelier-cli-proxy:prev-<ts>-selfupdate sichern (nicht `latest`: nach einem manuellen Build kann `latest` auf ein
#      anderes Image zeigen als der laufende Container, ein Rollback stellte dann ein Image wieder her, das nie lief). Ist keine
#      laufende ID ermittelbar, wird mit Logzeile `$IMAGE:latest` gesichert. Danach `docker compose build --no-cache cli-proxy`,
#      Dienst neu erstellen.
#   4. Prüfen: /health, dann je eine echte Anfrage über claude und codex mit Gateway-Schlüssel. Bestanden ist `smoke` nur bei
#      curl-Exit 0, HTTP 200, vorhandenem Header x-cli-proxy-provider == angefragtes Backend (sonst hätte der Failover einen
#      Defekt verdeckt), ohne Header x-cli-proxy-failover und mit der (wie beim Host-Smoke normalisierten) Antwort „pong“.
#      Der Gateway-Schlüssel steht nie in den Prozessargumenten: curl bekommt den Header aus einer Datei (-H @datei, Rechte 600,
#      wird sofort wieder gelöscht).
#   5. Fehler: altes Image zurück, Dienst neu erstellen, Telegram-Alarm, die Zielversionen werden in blocked-versions
#      gemerkt und nicht jede Nacht neu versucht (FORCE=1 hebt das auf). Rollback und Build-Fehlerpfad taggen $IMAGE:latest auf
#      die in Schritt 3 erfasste ID zurück. Der Rollback endet nur dann mit Status 1, wenn das Zurück-Taggen und `compose up`
#      gelingen, /health wieder ok meldet UND der laufende Container genau diese ID hat; sonst Status 2. Das gilt auch für das
#      Zurück-Taggen im Build-Fehlerpfad.
#   6. Erfolg: Telegram-Meldung mit Versionen und geänderter Modellliste. Hat sich die Modellliste geändert, wird openwebui neu
#      gestartet (Open WebUI lässt bei Modelländerungen Datenbank-Handles offen, siehe websites/openwebui/CLAUDE.md).
#   7. Host-Abgleich (Vertrag: reports/2026-11-23-modellmatrix-d066-architektur-und-verlauf.md, §8.4). Er läuft nach dem Lock in JEDEM Ausstiegszweig über die gemeinsame
#      Funktion `finish <rc>` (Versionsermittlung gescheitert, alles aktuell, DRY_RUN, gesperrte Zielversionen, Gateway belegt,
#      Build fehlgeschlagen, Rollback, Erfolg), nicht nur nach einem Container-Update. Er richtet sich nach der im Container
#      laufenden, dort schon getesteten Version. `finish` endet mit dem schlechtesten Status aus Container-Teil und Host-Abgleich.
#      - Die claude- und codex-CLI unter ~/.npm-global werden auf die Container-Version gebracht (nur vorwärts). Danach wird die
#        installierte Version nachgelesen.
#      - Danach wird das Host-Standardmodell von codex (~/.codex/config.toml) auf die neueste Sol-Version aus
#        ~/.codex/models_cache.json gestellt. Der Top-Level-Schlüssel `model` wird per TOML-Parser (tomllib) gesetzt oder vor dem
#        ersten [abschnitt] eingefügt (Schlüssel in [profiles.x] bleiben unberührt), über eine Temp-Datei plus mv geschrieben
#        und anschließend nachgelesen (model == neueste Version). Rechte und übriger Inhalt bleiben unverändert.
#        Bewusste Grenze (Q7): Ist `model` ein mehrzeiliger TOML-String, wird nichts umgebaut; die Gegenprüfung per tomllib
#        lehnt das Ergebnis ab (fail-safe), die Datei bleibt byte-identisch, es gibt einen Alarm und Status 1.
#      - Jeder Schritt wird mit einer echten Anfrage geprüft (Exit-Code UND nur die letzte Modellantwort, Vergleich ohne Groß-/
#        Kleinschreibung und Satzzeichen; bei codex über `--output-last-message`, weil der Gesamt-Output den Prompt mit „pong“
#        enthält). Bei Fehlschlag wird zurückgerollt (alte npm-Version bzw. Sicherung der config.toml). Gescheiterte Backups,
#        Schreibvorgänge und Rollbacks werden jeweils gemeldet; ein gescheiterter Rollback löst einen eigenen Alarm aus.
#      - Zielkombination statt Einzelschritte: Lehnt die neue codex-CLI das in config.toml konfigurierte (alte) Modell ab, wäre
#        sie allein am Modell gescheitert und würde zurückgerollt; danach scheiterte der Modellwechsel an der alten CLI, und
#        beide Änderungen hoben sich gegenseitig auf. Deshalb folgt vor dem CLI-Rollback ein zweiter Smoke derselben neuen CLI
#        mit dem Zielmodell (`codex exec -m <ziel>`, config.toml bleibt unberührt). Das Zielmodell ist die neueste Sol-Version
#        aus models_cache.json, die erst jetzt gelesen wird, weil der erste Lauf der neuen CLI den Cache auffrischt. Besteht der
#        zweite Smoke, bleibt die neue CLI installiert (Logzeile und Alarm sagen das), und der anschließende Modell-Abgleich
#        stellt config.toml um. Nur wenn auch der zweite Smoke scheitert (oder es kein anderes Zielmodell gibt), wird
#        zurückgerollt. Die Erfolgsmeldung zur CLI wird in diesem Fall erst nach dem Modellwechsel gesendet. Scheitert der
#        Modellwechsel danach (Backup, Schreiben, Nachlesen, abschließender Smoke), ist die config.toml wieder auf dem alten
#        Modell, das die neue CLI ablehnt. Dann wird auch die CLI auf die alte Version zurückgesetzt (die mit dem alten Modell
#        nachweislich läuft), mit eigenem Alarm; ist dieser Rollback gescheitert, endet das Skript mit Status 2. War der
#        Modellwechsel selbst unrettbar (Status 2, config.toml womöglich verändert), bleibt die CLI unangetastet.
#      Warum: Der Server gibt neue Modelle nur an aktuelle CLI-Versionen frei. Am 2026-11-23 lehnte die veraltete Host-CLI
#      (0.151) gpt-6.2-sol ab und legte codex auf dem Host lahm, während der Container es längst bediente.
#
# Aufruf:  scripts/cli-proxy-selfupdate.sh            (Cron: 40 3 * * *, Log /srv/backup/cli-proxy-update/update.log)
#          FORCE=1 scripts/cli-proxy-selfupdate.sh    auch ohne neue Version bauen, nicht auf Ruhe warten, Sperre ignorieren
#          DRY_RUN=1 scripts/cli-proxy-selfupdate.sh  nur vergleichen und melden (der Host-Abgleich läuft trocken mit)
#          HOST_ONLY=1 scripts/cli-proxy-selfupdate.sh nur den Host-Abgleich (Schritt 7), Exit-Code = Status des Abgleichs
# Exit-Code: 0 = alles in Ordnung (auch übersprungene und ausgesetzte Nächte), 1 = Fehlschlag mit erfolgreichem Rollback, 2 = Rollback gescheitert.
# Testbar: Das Skript ist sourcebar (`main` läuft nur bei direktem Aufruf). Per Umgebung überschreibbar: PROJECT_DIR, CONTAINER,
#          IMAGE, STATE_DIR, LOG, GATEWAY, HOST_NPM_PREFIX, HOST_CODEX_CONFIG, HOST_CODEX_MODELS, SELFUPDATE_PATH (PATH) und
#          ALARM_CMD (Standard: `hermes send --to telegram`, bekommt den Text als letztes Argument).
#          Tests: cli-proxy/tests/test_selfupdate_script.py (Fake-CLIs, kein Docker, kein npm, kein Telegram).
# Rückfall von Hand: docker tag geef-atelier-cli-proxy:prev-<ts>-selfupdate geef-atelier-cli-proxy:latest &&
#          (cd /srv/docker/websites/geef_atelier && docker compose up -d --no-deps --no-build cli-proxy)

PROJECT_DIR=${PROJECT_DIR:-/srv/docker/websites/geef_atelier}
CONTAINER=${CONTAINER:-geef-atelier-cli-proxy}
IMAGE=${IMAGE:-geef-atelier-cli-proxy}
STATE_DIR=${STATE_DIR:-/srv/backup/cli-proxy-update}
LOG=${LOG:-$STATE_DIR/update.log}
BLOCKED=$STATE_DIR/blocked-versions
GATEWAY=${GATEWAY:-http://localhost:8090}
WAIT_QUIET_SECONDS=${WAIT_QUIET_SECONDS:-1800}
KEEP_PREV=3
TS=$(date +%Y%m%d-%H%M%S)

HOST_NPM_PREFIX=${HOST_NPM_PREFIX:-/home/developer/.npm-global}
HOST_CODEX_CONFIG=${HOST_CODEX_CONFIG:-/home/developer/.codex/config.toml}
HOST_CODEX_MODELS=${HOST_CODEX_MODELS:-/home/developer/.codex/models_cache.json}
ALARM_CMD=${ALARM_CMD:-hermes send --to telegram}
SMOKE_PROMPT="Antworte nur mit dem Wort: pong"

declare -A PKG=([claude]=@anthropic-ai/claude-code [codex]=@openai/codex [gemini]=@google/gemini-cli)
declare -A OLD NEW
CODEX_CLI_PENDING=""

log() { echo "[$(date '+%F %T')] $*"; }

alarm() {
  local text="cli-proxy-Selfupdate $(hostname -s) $(date '+%F %H:%M'): $*" cmd
  read -ra cmd <<< "$ALARM_CMD"
  if "${cmd[@]}" "$text" > /dev/null 2>&1; then log "Meldung gesendet."; else log "Meldung konnte nicht gesendet werden: $*"; fi
}

semver_lt() { [[ $1 != "$2" && $(printf '%s\n%s\n' "$1" "$2" | sort -V | head -1) == "$1" ]]; }
host_version() { "$HOST_NPM_PREFIX/bin/$1" --version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1; }
container_version() { docker exec "$CONTAINER" "$1" --version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1; }

# Antwort für den Vergleich normalisieren: klein, ohne Satzzeichen, Leerraum zusammengezogen.
normalize_answer() {
  python3 -c 'import sys, unicodedata; t = "".join(c for c in sys.argv[1].lower() if not unicodedata.category(c).startswith("P")); print(" ".join(t.split()))' "$1"
}

# Gemeinsame Prüfung für Host- und Container-Smoke: Exit-Code 0 UND die (normalisierte) Antwort lautet genau „pong“.
smoke_answer_ok() { (( $1 == 0 )) && [[ $(normalize_answer "$2") == pong ]]; }

# Echte Anfrage an die Host-CLI. Erfolg nur bei Exit-Code 0 UND einer letzten Modellantwort, die (normalisiert) „pong“ lautet.
# codex gibt den Prompt im Kopfblock des Gesamt-Outputs aus, deshalb wird dort ausschließlich die letzte Nachricht gelesen
# (--output-last-message). claude -p schreibt nur die Antwort nach stdout.
# Optionaler zweiter Parameter (nur codex): Modell, das per `-m` erzwungen wird, ohne config.toml zu ändern.
host_smoke() {
  local cli=$1 model=${2:-} dir rc=0 answer="" detail="" label=""
  local -a margs=()
  if [[ -n $model && $cli == codex ]]; then margs=(-m "$model"); label=" (Modell $model)"; fi
  dir=$(mktemp -d) || { log "Host-Smoke $cli fehlgeschlagen: Temp-Verzeichnis nicht anlegbar."; return 1; }
  case $cli in
    codex)
      (cd "$dir" && timeout 180 "$HOST_NPM_PREFIX/bin/codex" exec --skip-git-repo-check --ephemeral \
        ${margs[@]+"${margs[@]}"} --output-last-message "$dir/last.txt" "$SMOKE_PROMPT" < /dev/null > "$dir/full.txt" 2>&1) || rc=$?
      [[ -f $dir/last.txt ]] && answer=$(cat "$dir/last.txt")
      detail=$(tail -3 "$dir/full.txt" 2>/dev/null | tr '\n' ' ' | cut -c1-300)
      ;;
    claude)
      answer=$(cd "$dir" && timeout 180 "$HOST_NPM_PREFIX/bin/claude" -p --model haiku "$SMOKE_PROMPT" < /dev/null 2> "$dir/err.txt") || rc=$?
      detail=$(tail -3 "$dir/err.txt" 2>/dev/null | tr '\n' ' ' | cut -c1-300)
      ;;
    *) rm -rf "$dir"; log "Host-Smoke: unbekannte CLI '$cli'."; return 1 ;;
  esac
  rm -rf "$dir"
  if smoke_answer_ok "$rc" "$answer"; then log "Host-Smoke $cli: ok$label"; return 0; fi
  log "Host-Smoke $cli fehlgeschlagen$label: Exit $rc, Antwort='$(cut -c1-80 <<< "$answer" | tr '\n' ' ')', ${detail:-keine Ausgabe}"
  return 1
}

# Laufende Host-Prozesse der npm-Installation (nur lesen, nie beenden). claude-Sitzungen laufen praktisch immer; npm ersetzt
# die Dateien atomar, laufende Prozesse behalten ihre geladene Version. Für codex wird trotzdem auf Ruhe gewartet, weil
# einzelne `codex exec`-Läufe (BookForge-Reviews) mitten im Update keine halb ersetzte Installation sehen sollen.
host_codex_busy() { pgrep -u "$(id -un)" -f "$HOST_NPM_PREFIX/lib/node_modules/@openai/codex/" > /dev/null; }

host_npm_install() { npm install -g --prefix "$HOST_NPM_PREFIX" "${PKG[$1]}@$2" >> "$LOG" 2>&1; }

# Zweiter Smoke der (bereits installierten) neuen codex-CLI mit dem Zielmodell, wenn der Smoke mit dem konfigurierten Modell
# scheiterte. Das Zielmodell wird hier gelesen, nicht früher: der erste Lauf der neuen CLI frischt models_cache.json auf.
# Rückgabe 0 nur, wenn ein vom konfigurierten verschiedenes Zielmodell existiert UND die neue CLI damit besteht. Setzt dann
# CODEX_TARGET_MODEL und CODEX_CONFIGURED_MODEL. config.toml wird nicht verändert.
smoke_codex_target_model() {
  local configured target
  configured=$(toml_model get "$HOST_CODEX_CONFIG" 2>/dev/null) || configured=""
  if ! target=$(newest_sol_model 2>/dev/null) || [[ -z $target ]]; then
    log "Host-Abgleich codex: kein Zielmodell aus models_cache.json ermittelbar, kein zweiter Smoke."
    return 1
  fi
  if [[ $target == "$configured" ]]; then
    log "Host-Abgleich codex: Zielmodell $target ist bereits konfiguriert, kein zweiter Smoke."
    return 1
  fi
  log "Host-Abgleich codex: Smoke mit konfiguriertem Modell '${configured:-<nicht gesetzt>}' gescheitert, zweiter Smoke der neuen CLI mit Zielmodell $target."
  host_smoke codex "$target" || return 1
  CODEX_TARGET_MODEL=$target
  CODEX_CONFIGURED_MODEL=$configured
}

# Rückgabe: 0 = in Ordnung/übersprungen, 1 = Update gescheitert, zurückgerollt, 2 = Update gescheitert UND Rollback gescheitert.
sync_host_cli() {
  local cli=$1 have want reason="" now new_model_note=""
  have=$(host_version "$cli"); want=$(container_version "$cli")
  if [[ -z $have || -z $want ]]; then log "Host-Abgleich $cli: Version nicht ermittelbar (Host='$have', Container='$want'), übersprungen."; return 0; fi
  if ! semver_lt "$have" "$want"; then log "Host-Abgleich $cli: aktuell ($have, Container $want)."; return 0; fi
  if [[ $cli == codex ]]; then
    local waited=0
    while host_codex_busy; do
      if (( waited >= 600 )); then log "Host-Abgleich codex: Host-codex nach 600s noch belegt, diese Nacht übersprungen."; return 0; fi
      sleep 30; waited=$((waited + 30))
    done
  fi
  log "Host-Abgleich $cli: $have → $want"
  if [[ ${DRY_RUN:-0} == 1 ]]; then log "DRY_RUN, keine Änderung."; return 0; fi
  if ! host_npm_install "$cli" "$want"; then
    reason="npm install fehlgeschlagen"
  else
    now=$(host_version "$cli")
    if [[ $now != "$want" ]]; then reason="nach npm install ist '${now:-keine}' installiert statt $want"
    elif ! host_smoke "$cli"; then
      if [[ $cli == codex ]] && smoke_codex_target_model; then
        new_model_note=1
        log "Host-Abgleich codex: neue CLI $want bleibt installiert (Zielkombination mit $CODEX_TARGET_MODEL getestet)."
      else
        reason="Testanfrage fehlgeschlagen"
      fi
    fi
  fi
  if [[ -n $reason ]]; then rollback_host_cli "$cli" "$have" "$want" "$reason"; return; fi
  if [[ -n $new_model_note ]]; then
    CODEX_CLI_PENDING="$have|$want|$CODEX_TARGET_MODEL|${CODEX_CONFIGURED_MODEL:-<nicht gesetzt>}|$new_model_note"
    return 0
  fi
  alarm "Host-CLI $cli aktualisiert: $have → $want (Container-Stand)."
}

# Setzt die Host-CLI auf die alte Version zurück. Rückgabe: 1 = zurückgesetzt, 2 = Rollback gescheitert.
rollback_host_cli() {
  local cli=$1 have=$2 want=$3 reason=$4
  log "Host-Abgleich $cli: $reason, Rollback auf $have."
  if host_npm_install "$cli" "$have" && [[ $(host_version "$cli") == "$have" ]]; then
    alarm "Host-CLI $cli: Update $have → $want fehlgeschlagen ($reason), auf $have zurückgesetzt. Details $LOG"
    return 1
  fi
  alarm "Host-CLI $cli: Update $have → $want fehlgeschlagen ($reason) UND ROLLBACK auf $have FEHLGESCHLAGEN (installiert: '$(host_version "$cli")'). Host-CLI womöglich defekt, bitte sofort prüfen. Details $LOG"
  return 2
}

# Gibt die neueste gelistete Sol-Version aus (leer, wenn die Datei gültig ist, aber keine enthält).
# Exit 0 = ausgewertet, 2 = Datei nicht lesbar oder kein gültiges JSON mit "models"-Liste (Fehlertext auf stderr).
newest_sol_model() {
  python3 - "$HOST_CODEX_MODELS" <<'PY'
import json, re, sys
try:
    data = json.load(open(sys.argv[1], encoding="utf-8"))
    if not isinstance(data, dict) or not isinstance(data.get("models"), list):
        raise ValueError("no 'models' list")
except Exception as exc:
    print(f"models_cache.json nicht auswertbar: {exc}", file=sys.stderr)
    sys.exit(2)
best = None
for entry in data.get("models", []):
    slug = (entry.get("slug") or "").strip() if isinstance(entry, dict) else ""
    m = re.fullmatch(r"gpt-(\d+(?:\.\d+)*)-sol", slug)
    if not m or entry.get("visibility") != "list":
        continue
    version = tuple(int(p) for p in m.group(1).split("."))
    version += (0,) * (4 - len(version))
    if best is None or version > best[0]:
        best = (version, slug)
print(best[1] if best else "")
PY
}

# toml_model get <config>                  gibt den Top-Level-Schlüssel `model` aus (leer, wenn nicht gesetzt).
# toml_model set <config> <neu> <ausgabe>  schreibt die geänderte Fassung in <ausgabe> (die Konfiguration bleibt unberührt),
#                                          setzt `model` vor der ersten [abschnitt]-Zeile bzw. fügt ihn dort ein und prüft per
#                                          tomllib, dass sich genau dieser Schlüssel geändert hat.
# Exit 0 = ok, 2 = TOML nicht lesbar oder Ergebnis ungültig.
toml_model() {
  python3 -c '
import os, re, sys, tomllib

def fail(msg):
    print("toml_model: " + msg)
    sys.exit(2)

def load(text):
    try:
        return tomllib.loads(text)
    except tomllib.TOMLDecodeError as exc:
        fail("TOML ungültig: %s" % exc)

def first_table_offset(text):
    for m in re.finditer(r"^[ \t]*\[", text, re.M):
        try:
            tomllib.loads(text[:m.start()])
        except tomllib.TOMLDecodeError:
            continue
        return m.start()
    return len(text)

mode, path = sys.argv[1], sys.argv[2]
try:
    text = open(path, "rb").read().decode("utf-8")
except (OSError, UnicodeDecodeError) as exc:
    fail("nicht lesbar: %s" % exc)
data = load(text)

if mode == "get":
    value = data.get("model", "")
    if not isinstance(value, str):
        fail("model ist kein String")
    print(value)
    sys.exit(0)

new, out = sys.argv[3], sys.argv[4]
quoted = "\"" + new.replace("\\", "\\\\").replace("\"", "\\\"") + "\""
pos = first_table_offset(text)
head, tail = text[:pos], text[pos:]
eol = "\r\n" if "\r\n" in text else "\n"
line = re.compile(r"^([ \t]*)(?:model|\"model\"|\x27model\x27)[ \t]*=[^\r\n]*(\r?\n|$)", re.M)
m = line.search(head)
if m:
    head = head[:m.start()] + m.group(1) + "model = " + quoted + m.group(2) + head[m.end():]
else:
    stripped = head.rstrip(" \t\r\n")
    rest = head[len(stripped):] if tail else ""
    if stripped:
        head = stripped + eol + "model = " + quoted + eol + rest
    else:
        head = "model = " + quoted + eol + (eol if tail else "")
result = head + tail
expected = dict(data)
expected["model"] = new
if load(result) != expected:
    fail("Ergebnis weicht von der erwarteten Konfiguration ab, nichts geschrieben")
with open(out, "wb") as fh:
    fh.write(result.encode("utf-8"))
    fh.flush()
    os.fsync(fh.fileno())
' "$@"
}

# Stellt die Sicherung atomar (Temp-Datei plus mv) wieder her und prüft das Ergebnis byteweise.
restore_host_config() {
  local backup=$1 tmp
  tmp=$(mktemp "$HOST_CODEX_CONFIG.tmp-XXXXXX") || return 1
  if cp -p "$backup" "$tmp" && mv -f "$tmp" "$HOST_CODEX_CONFIG" && cmp -s "$backup" "$HOST_CODEX_CONFIG"; then return 0; fi
  rm -f "$tmp"
  return 1
}

# Rückgabe: 0 = in Ordnung/übersprungen, 1 = Wechsel gescheitert, Ausgangsstand wiederhergestellt bzw. nie verändert,
# 2 = Wechsel gescheitert UND Wiederherstellung gescheitert.
sync_host_codex_model() {
  local current newest backup tmp now
  [[ -f $HOST_CODEX_CONFIG && -f $HOST_CODEX_MODELS ]] || { log "Host-Modell: config.toml oder models_cache.json fehlt, übersprungen."; return 0; }
  if ! current=$(toml_model get "$HOST_CODEX_CONFIG"); then
    log "Host-Modell: config.toml nicht auswertbar: $current"
    alarm "Host-Standardmodell codex: config.toml nicht auswertbar, nichts geändert. Details $LOG"
    return 1
  fi
  if ! newest=$(newest_sol_model); then
    log "Host-Modell: models_cache.json nicht auswertbar, nichts geändert."
    alarm "Host-Standardmodell codex: models_cache.json nicht auswertbar, nichts geändert. Details $LOG"
    return 1
  fi
  if [[ -z $newest ]]; then log "Host-Modell: keine Sol-Version in models_cache.json, übersprungen."; return 0; fi
  if [[ $current == "$newest" ]]; then log "Host-Modell: aktuell ($current)."; return 0; fi
  log "Host-Modell: ${current:-<nicht gesetzt>} → $newest"
  if [[ ${DRY_RUN:-0} == 1 ]]; then log "DRY_RUN, keine Änderung."; return 0; fi

  backup="$HOST_CODEX_CONFIG.bak-selfupdate-$TS"
  if ! cp -p "$HOST_CODEX_CONFIG" "$backup" || ! cmp -s "$HOST_CODEX_CONFIG" "$backup"; then
    rm -f "$backup"
    alarm "Host-Standardmodell codex: Sicherung der config.toml fehlgeschlagen, Wechsel auf $newest nicht durchgeführt (nichts geändert). Details $LOG"
    return 1
  fi
  tmp=$(mktemp "$HOST_CODEX_CONFIG.tmp-XXXXXX") || tmp=""
  if [[ -z $tmp ]] || ! toml_model set "$HOST_CODEX_CONFIG" "$newest" "$tmp" \
      || ! chmod --reference="$HOST_CODEX_CONFIG" "$tmp" || ! mv -f "$tmp" "$HOST_CODEX_CONFIG"; then
    [[ -n $tmp ]] && rm -f "$tmp"
    alarm "Host-Standardmodell codex: Schreiben der config.toml für $newest fehlgeschlagen, Ausgangsstand ($current) unverändert. Details $LOG"
    return 1
  fi
  now=$(toml_model get "$HOST_CODEX_CONFIG") || now=""
  if [[ $now != "$newest" ]]; then
    log "Host-Modell: Nachlesen ergab '$now' statt '$newest'."
    if restore_host_config "$backup"; then
      alarm "Host-Standardmodell codex: Nachlesen der config.toml ergab '$now' statt '$newest', $current wiederhergestellt. Details $LOG"
      return 1
    fi
    alarm "Host-Standardmodell codex: Nachlesen ergab '$now' statt '$newest' UND WIEDERHERSTELLUNG der config.toml FEHLGESCHLAGEN (Sicherung $backup). Bitte sofort prüfen. Details $LOG"
    return 2
  fi
  if ! host_smoke codex; then
    if restore_host_config "$backup"; then
      alarm "Host-Standardmodell codex: Wechsel $current → $newest fehlgeschlagen, $current wiederhergestellt. Details $LOG"
      return 1
    fi
    alarm "Host-Standardmodell codex: Wechsel $current → $newest fehlgeschlagen UND WIEDERHERSTELLUNG der config.toml FEHLGESCHLAGEN (Sicherung $backup). Bitte sofort prüfen. Details $LOG"
    return 2
  fi
  alarm "Host-Standardmodell codex umgestellt: ${current:-<nicht gesetzt>} → $newest."
  ls -1t "$HOST_CODEX_CONFIG".bak-selfupdate-* 2>/dev/null | tail -n +4 | xargs -r rm -f
  return 0
}

# Schlechtester Status aller Schritte.
sync_host() {
  local rc=0 r
  r=0; sync_host_cli codex || r=$?; (( r > rc )) && rc=$r
  r=0; sync_host_cli claude || r=$?; (( r > rc )) && rc=$r
  r=0; sync_host_codex_model || r=$?; (( r > rc )) && rc=$r
  if [[ -n $CODEX_CLI_PENDING ]]; then
    local have want target configured _
    IFS='|' read -r have want target configured _ <<< "$CODEX_CLI_PENDING"
    CODEX_CLI_PENDING=""
    if (( r == 0 )); then
      alarm "Host-CLI codex aktualisiert: $have → $want (Container-Stand). Der Smoke mit dem bisherigen Modell $configured scheiterte, mit dem Zielmodell $target bestand die neue CLI; kein Rollback, config.toml wurde im Modell-Abgleich umgestellt."
    elif (( r == 1 )); then
      local r2=0
      log "Host-Abgleich codex: Modellwechsel auf $target gescheitert, die neue CLI $want akzeptiert nur das Zielmodell, config.toml hat wieder $configured."
      rollback_host_cli codex "$have" "$want" "neue CLI läuft nur mit Zielmodell $target, der Modellwechsel scheiterte, config.toml hat wieder $configured" || r2=$?
      (( r2 > rc )) && rc=$r2
    else
      log "Host-Abgleich codex: Modellwechsel auf $target unrettbar gescheitert, CLI $want bleibt (Zustand der config.toml ungewiss)."
      alarm "Host-CLI codex $have → $want: nur mit Zielmodell $target lauffähig, aber der Modellwechsel und die Wiederherstellung der config.toml sind gescheitert. CLI nicht zurückgesetzt, weil der Stand der config.toml ungewiss ist. Bitte sofort prüfen. Details $LOG"
    fi
  fi
  return "$rc"
}

# Gemeinsamer Ausstieg nach dem Lock: Host-Abgleich (bei DRY_RUN trocken), dann der schlechtere der beiden Status.
# Optionaler zweiter Parameter: Zusatz für die Schlusszeile (z. B. „ausgesetzt“), ändert den Exit-Code nicht.
finish() {
  local rc=${1:-0} label=${2:-} hrc=0
  sync_host || hrc=$?
  (( hrc > rc )) && rc=$hrc
  log "Fertig (Status $rc${label:+, $label})."
  exit "$rc"
}

# Pfade, die cli-proxy/Dockerfile per COPY ins Image holt (Build-Kontext cli-proxy/, keine .dockerignore). Muss zum Dockerfile
# passen; test_selfupdate_script.py prüft das.
IMAGE_SOURCE_PATHS=(cli-proxy/pyproject.toml cli-proxy/src)

# Ignorierte Dateien, die trotz .gitignore ins Image gelangen würden. Harmlos und nie gezählt: Python-Bytecode-Caches
# (__pycache__/), .pytest_cache/ und *.egg-info/ (entsteht bei `pip install -e .` im Arbeitsverzeichnis). Ein *.pyc außerhalb
# von __pycache__/ zählt, weil Python es ohne Quelldatei importiert. Exit 0 = Liste auf stdout, 1 = git gescheitert.
image_ignored_files() {
  local out line
  out=$(git -C "$PROJECT_DIR" ls-files --others --ignored --exclude-standard -- "${IMAGE_SOURCE_PATHS[@]}" 2>&1) || { printf '%s\n' "$out"; return 1; }
  while IFS= read -r line; do
    [[ -z $line || $line =~ (^|/)(__pycache__|\.pytest_cache|[^/]+\.egg-info)/ ]] && continue
    printf '%s\n' "$line"
  done <<< "$out"
}

# Prüft, dass cli-proxy/ dem committeten Stand entspricht. Setzt TREE_PROBLEM (leer = sauber, sonst Grund für Log und Meldung)
# und TREE_ID (`git rev-parse HEAD:cli-proxy`). Rückgabe 0 = sauber, 1 = nicht sauber oder nicht prüfbar (fail-closed).
cli_proxy_tree_check() {
  local out ignored
  TREE_PROBLEM=""; TREE_ID=""
  if ! out=$(git -C "$PROJECT_DIR" status --porcelain -- cli-proxy 2>&1); then
    TREE_PROBLEM="Git-Stand von cli-proxy/ nicht prüfbar ($(head -1 <<< "$out"))"; return 1
  fi
  if [[ -n $out ]]; then
    TREE_PROBLEM="nicht committete Änderungen unter cli-proxy/ ($(wc -l <<< "$out") Einträge: $(head -5 <<< "$out" | tr '\n' ' '))"; return 1
  fi
  if ! ignored=$(image_ignored_files); then
    TREE_PROBLEM="ignorierte Dateien unter cli-proxy/ nicht prüfbar ($(head -1 <<< "$ignored"))"; return 1
  fi
  if [[ -n $ignored ]]; then
    TREE_PROBLEM="ignorierte, aber ins Image kopierte Dateien unter cli-proxy/ ($(wc -l <<< "$ignored") Einträge: $(head -5 <<< "$ignored" | tr '\n' ' '))"; return 1
  fi
  if ! TREE_ID=$(git -C "$PROJECT_DIR" rev-parse --verify --quiet HEAD:cli-proxy 2>/dev/null) || [[ -z $TREE_ID ]]; then
    TREE_ID=""; TREE_PROBLEM="committeter Baum von cli-proxy/ nicht ermittelbar"; return 1
  fi
  return 0
}

# Der Neubau nimmt das Arbeitsverzeichnis als Build-Kontext. Nur ein committeter Stand von cli-proxy/ darf an die Abnehmer
# (BookForge, Hermes, DMS, Open WebUI) gehen. Rückgabe 0 = sauber; sonst ist der Grund geloggt und gemeldet.
# Parameter: Prüfzeitpunkt für die Logzeile.
cli_proxy_tree_clean() {
  local phase=${1:-vor dem Bauen}
  cli_proxy_tree_check && return 0
  log "Image-Neubau ausgesetzt ($phase): $TREE_PROBLEM"
  alarm "Image-Neubau ausgesetzt ($SUMMARY): $TREE_PROBLEM. Erst committen oder aufräumen, dann baut die nächste Nacht. Der Host-Abgleich läuft weiter, Details $LOG"
  return 1
}

# Letzte Prüfung zwischen Build und `compose up`: Der gebaute Stand muss noch der geprüfte sein. Sonst kein Austausch,
# $IMAGE:latest zurück auf das laufende Image. Kehrt nur bei sauberem, unverändertem Stand zurück.
guard_before_swap() {
  local problem
  if cli_proxy_tree_check; then
    [[ $TREE_ID == "$BUILD_TREE_ID" ]] && return 0
    problem="committeter Baum von cli-proxy/ während des Builds gewechselt (${BUILD_TREE_ID:0:12} → ${TREE_ID:0:12})"
  else
    problem=$TREE_PROBLEM
  fi
  log "Kein Austausch des Containers: $problem. $IMAGE:latest wird auf ${ROLLBACK_ID:-$IMAGE:$PREV_TAG} zurückgesetzt."
  if docker tag "$ROLLBACK_REF" "$IMAGE:latest"; then
    alarm "Image-Neubau verworfen ($SUMMARY): $problem. Der laufende Container blieb unverändert, die nächste Nacht baut neu. Details $LOG"
    finish 0 ausgesetzt
  fi
  alarm "Image-Neubau verworfen ($SUMMARY): $problem. Container nicht ausgetauscht, aber Zurück-Taggen von $IMAGE:latest auf ${ROLLBACK_ID:-$IMAGE:$PREV_TAG} FEHLGESCHLAGEN. Ein späteres compose up würde das ungeprüfte Image starten, bitte sofort prüfen. Details $LOG"
  finish 2
}

busy() {
  docker exec "$CONTAINER" sh -c 'n=0; for p in /proc/[0-9]*; do read -r c < "$p/comm" 2>/dev/null || continue; case $c in claude|codex|gemini|node) n=$((n+1));; esac; done; echo $n' 2>/dev/null || echo 0
}

models_snapshot() {
  { curl -s -m 30 "$GATEWAY/v1/claude/models"; echo; curl -s -m 30 "$GATEWAY/v1/codex/models"; } \
    | python3 -c '
import sys,json
ids=set()
for line in sys.stdin:
    line=line.strip()
    if not line: continue
    try: d=json.loads(line)
    except Exception: continue
    ids.update(m["id"] for m in d.get("data",[]))
    for k,v in (d.get("x_aliases") or {}).items(): ids.add(k+"="+v)
print("\n".join(sorted(ids)))'
}

rollback() {
  local ok=no tagged=ja up=ja same=nein rc=1 ctr_id
  log "ROLLBACK: $1"
  docker tag "$ROLLBACK_REF" "$IMAGE:latest" || { tagged=nein; log "Rollback: docker tag fehlgeschlagen."; }
  (cd "$PROJECT_DIR" && docker compose up -d --no-deps --no-build cli-proxy) >> "$LOG" 2>&1 || { up=nein; log "Rollback: docker compose up fehlgeschlagen."; }
  for _ in $(seq 1 30); do
    curl -s -m 5 "$GATEWAY/health" | grep -q '"status":"ok"' && { ok=yes; break; }; sleep 3
  done
  ctr_id=$(docker inspect --format '{{.Image}}' "$CONTAINER" 2>/dev/null)
  if [[ -n $ctr_id && $ctr_id == "$ROLLBACK_ID" ]]; then same=ja; else log "Rollback: Container-Image '${ctr_id:-?}' != Sicherung '${ROLLBACK_ID:-?}'."; fi
  echo "$TARGET" > "$BLOCKED"
  if [[ $tagged == ja && $up == ja && $ok == yes && $same == ja ]]; then
    alarm "Update fehlgeschlagen ($SUMMARY): $1. Altes Image zurückgespielt: $tagged, Gateway wieder gesund: $ok. Zielversionen gesperrt, Details $LOG"
  else
    rc=2
    alarm "Update fehlgeschlagen ($SUMMARY): $1. ROLLBACK GESCHEITERT (Zurück-Taggen: $tagged, compose up: $up, Gateway gesund: $ok, Container hat altes Image: $same). Gateway womöglich defekt, bitte sofort prüfen. Zielversionen gesperrt, Details $LOG"
  fi
  finish "$rc"
}

wait_healthy() {
  local h
  for _ in $(seq 1 40); do
    h=$(curl -s -m 5 "$GATEWAY/health" || true)
    if grep -q '"status":"ok"' <<< "$h" && grep -q '"claude":"ready"' <<< "$h" && grep -q '"codex":"ready"' <<< "$h"; then return 0; fi
    sleep 3
  done
  return 1
}

# Echte Anfrage über das Gateway. Der Schlüssel wird nie als Argument übergeben, sondern über eine Header-Datei (curl >= 7.55).
# Bestanden nur bei: curl-Exit 0, HTTP 200, Header x-cli-proxy-provider vorhanden und == Backend, kein x-cli-proxy-failover,
# normalisierte Antwort == pong (smoke_answer_ok, dieselbe Prüfung wie beim Host-Smoke).
smoke() {
  local backend=$1 model=$2 key hdr body hfile crc=0
  local -a auth=()
  key=$(grep -E '^CLI_PROXY_API_KEY=' "$PROJECT_DIR/.env" 2>/dev/null | head -1 | cut -d= -f2-)
  hdr=$(mktemp); body=$(mktemp); hfile=$(mktemp)
  if [[ -n $key ]]; then
    printf 'Authorization: Bearer %s\n' "$key" > "$hfile"
    auth=(-H "@$hfile")
  fi
  curl -s -m 180 -D "$hdr" -o "$body" -X POST "$GATEWAY/v1/$backend/chat/completions" \
    "${auth[@]}" -H 'Content-Type: application/json' \
    -d "{\"model\":\"$model\",\"messages\":[{\"role\":\"user\",\"content\":\"$SMOKE_PROMPT\"}]}" || crc=$?
  rm -f "$hfile"
  unset key
  local code provider content failover="" has_failover=0
  local -a why=()
  code=$(head -1 "$hdr" 2>/dev/null | awk '{print $2}')
  provider=$(grep -i '^x-cli-proxy-provider:' "$hdr" 2>/dev/null | head -1 | tr -d '\r' | awk '{print tolower($2)}')
  if grep -qi '^x-cli-proxy-failover:' "$hdr" 2>/dev/null; then
    has_failover=1
    failover=$(grep -i '^x-cli-proxy-failover:' "$hdr" | head -1 | tr -d '\r' | cut -d: -f2- | tr -d ' ')
  fi
  content=$(python3 -c 'import sys,json;print((json.load(open(sys.argv[1]))["choices"][0]["message"]["content"] or "").strip())' "$body" 2>/dev/null)
  rm -f "$hdr" "$body"
  (( crc == 0 )) || why+=("curl Exit $crc")
  [[ $code == 200 ]] || why+=("HTTP ${code:-?}")
  [[ -n $provider ]] || why+=("Header x-cli-proxy-provider fehlt")
  [[ -z $provider || $provider == "${backend,,}" ]] || why+=("Provider '$provider' statt '$backend'")
  (( has_failover == 0 )) || why+=("Failover-Header gesetzt ('${failover:-leer}')")
  smoke_answer_ok 0 "$content" || why+=("Antwort nicht 'pong'")
  log "Smoke $backend/$model: curl $crc, HTTP ${code:-?}, provider='${provider:-?}', failover='${failover:-nein}', Antwort='${content:0:40}'${why:+ -> FEHLGESCHLAGEN: ${why[*]}}"
  smoke_answer_ok "$crc" "$content" && (( ${#why[@]} == 0 ))
}

main() {
  set -uo pipefail
  umask 077
  export PATH=${SELFUPDATE_PATH:-/usr/local/bin:/usr/bin:/bin:/home/developer/.local/bin}

  mkdir -p -m 700 "$STATE_DIR" || { echo "STATE_DIR $STATE_DIR nicht anlegbar" >&2; exit 1; }
  [[ -f $LOG && $(stat -c %s "$LOG") -gt 2097152 ]] && mv "$LOG" "$LOG.1"
  exec >> "$LOG" 2>&1

  exec 9> "$STATE_DIR/lock"
  if ! flock -n 9; then log "Anderer Lauf aktiv, Abbruch."; exit 0; fi

  if [[ ${HOST_ONLY:-0} == 1 ]]; then log "Nur Host-Abgleich."; finish 0; fi

  local cli out now changed=0
  for cli in claude codex gemini; do
    out=$(docker exec "$CONTAINER" "$cli" --version 2>/dev/null | head -1)
    OLD[$cli]=$(grep -oE '[0-9]+\.[0-9]+\.[0-9]+' <<< "$out" | head -1)
    NEW[$cli]=$(timeout 60 npm view "${PKG[$cli]}" version 2>/dev/null | tail -1)
    if [[ -z ${OLD[$cli]} || -z ${NEW[$cli]} ]]; then
      log "Version von $cli nicht ermittelbar (installiert='${OLD[$cli]}', npm='${NEW[$cli]}'), Container-Teil ohne Änderung beendet."
      finish 0
    fi
  done
  SUMMARY="claude ${OLD[claude]}→${NEW[claude]}, codex ${OLD[codex]}→${NEW[codex]}, gemini ${OLD[gemini]}→${NEW[gemini]}"
  TARGET="claude=${NEW[claude]} codex=${NEW[codex]} gemini=${NEW[gemini]}"

  for cli in claude codex gemini; do [[ ${OLD[$cli]} != "${NEW[$cli]}" ]] && changed=1; done
  if [[ $changed == 0 && ${FORCE:-0} != 1 ]]; then
    log "Alles aktuell (claude ${OLD[claude]}, codex ${OLD[codex]}, gemini ${OLD[gemini]})."
    finish 0
  fi
  log "Update nötig: $SUMMARY"
  if [[ ${DRY_RUN:-0} == 1 ]]; then log "DRY_RUN, keine Änderung."; finish 0; fi

  if [[ ${FORCE:-0} != 1 && -f $BLOCKED && $(cat "$BLOCKED") == "$TARGET" ]]; then
    log "Diese Zielversionen ($TARGET) sind nach einem Fehlschlag gesperrt, überspringe. Entsperren: FORCE=1 oder $BLOCKED löschen."
    finish 0
  fi

  cli_proxy_tree_clean "vor der Ruhewartezeit" || finish 0 ausgesetzt

  if [[ ${FORCE:-0} != 1 ]]; then
    local waited=0
    while [[ $(busy) -gt 0 ]]; do
      if (( waited >= WAIT_QUIET_SECONDS )); then
        log "Gateway nach ${WAIT_QUIET_SECONDS}s noch belegt, diese Nacht übersprungen."
        finish 0
      fi
      sleep 30; waited=$((waited + 30))
    done
    (( waited > 0 )) && log "Gateway nach ${waited}s ruhig."
  fi

  BEFORE=$(models_snapshot)

  PREV_TAG="prev-$TS-selfupdate"
  RUNNING_ID=$(docker inspect -f '{{.Image}}' "$CONTAINER" 2>/dev/null | head -1)
  if [[ -n $RUNNING_ID ]]; then
    ROLLBACK_REF=$RUNNING_ID
    ROLLBACK_ID=$RUNNING_ID
    log "Laufender Container $CONTAINER nutzt Image $RUNNING_ID, dieses wird als Rollback-Ziel gesichert."
  else
    ROLLBACK_REF="$IMAGE:latest"
    ROLLBACK_ID=""
    log "Image-ID des laufenden Containers $CONTAINER nicht ermittelbar, Rückfall: Sicherung von $IMAGE:latest."
  fi
  docker tag "$ROLLBACK_REF" "$IMAGE:$PREV_TAG" || { log "Image-Sicherung fehlgeschlagen, Abbruch."; finish 1; }
  if [[ -z $ROLLBACK_ID ]]; then
    ROLLBACK_ID=$(docker image inspect --format '{{.Id}}' "$IMAGE:$PREV_TAG" 2>/dev/null)
    ROLLBACK_REF=${ROLLBACK_ID:-$IMAGE:$PREV_TAG}
  fi
  log "Altes Image ${ROLLBACK_ID:-<ID unbekannt>} gesichert als $IMAGE:$PREV_TAG"

  cli_proxy_tree_clean "unmittelbar vor dem Bauen" || finish 0 ausgesetzt
  BUILD_TREE_ID=$TREE_ID
  log "Baue Image neu (--no-cache) aus dem committeten Baum cli-proxy ${BUILD_TREE_ID:0:12}."
  if ! (cd "$PROJECT_DIR" && nice -n 10 docker compose build --no-cache cli-proxy) >> "$LOG" 2>&1; then
    echo "$TARGET" > "$BLOCKED"
    if docker tag "$ROLLBACK_REF" "$IMAGE:latest"; then
      alarm "Build fehlgeschlagen ($SUMMARY). Nichts geändert, Zielversionen gesperrt, Details $LOG"
      finish 1
    fi
    alarm "Build fehlgeschlagen ($SUMMARY) UND Zurück-Taggen auf ${ROLLBACK_ID:-$IMAGE:$PREV_TAG} (gesichert als $IMAGE:$PREV_TAG) FEHLGESCHLAGEN. $IMAGE:latest womöglich nicht das alte Image, bitte sofort prüfen. Zielversionen gesperrt, Details $LOG"
    finish 2
  fi

  guard_before_swap
  (cd "$PROJECT_DIR" && docker compose up -d --no-deps cli-proxy) >> "$LOG" 2>&1 || rollback "docker compose up fehlgeschlagen"
  wait_healthy || rollback "Gateway nach dem Neustart nicht gesund (/health)"

  for cli in claude codex gemini; do
    now=$(docker exec "$CONTAINER" "$cli" --version 2>/dev/null | head -1 | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1)
    log "Installiert nach Update: $cli $now (npm: ${NEW[$cli]})"
  done

  smoke claude claude-haiku-latest || rollback "Testanfrage über claude schlug fehl"
  # Pro-/Batch-Varianten sind im Abo nicht zwingend nutzbar (gpt-6.1-terra-pro scheitert 2026-11-10 auch mit dem alten Image);
  # getestet werden deshalb bis zu zwei normale Modelle, eines davon muss direkt über codex antworten.
  mapfile -t CODEX_CANDIDATES < <(curl -s -m 30 "$GATEWAY/v1/codex/models" | python3 -c '
import sys,json
ids=[m["id"] for m in json.load(sys.stdin)["data"] if ":" not in m["id"] and "-pro" not in m["id"]]
print("\n".join(ids[:2]))' 2>/dev/null)
  (( ${#CODEX_CANDIDATES[@]} > 0 )) || rollback "Codex-Modellliste leer"
  local codex_ok=0 cm
  for cm in "${CODEX_CANDIDATES[@]}"; do smoke codex "$cm" && { codex_ok=1; break; }; done
  (( codex_ok )) || rollback "Testanfrage über codex schlug fehl (${CODEX_CANDIDATES[*]})"

  rm -f "$BLOCKED"
  AFTER=$(models_snapshot)
  if [[ "$BEFORE" != "$AFTER" ]]; then
    ADDED=$(comm -13 <(echo "$BEFORE") <(echo "$AFTER") | tr '\n' ' ')
    REMOVED=$(comm -23 <(echo "$BEFORE") <(echo "$AFTER") | tr '\n' ' ')
    log "Modellliste geändert. Neu: ${ADDED:-keine}; entfallen: ${REMOVED:-keine}"
    if docker ps --format '{{.Names}}' | grep -qx openwebui; then
      docker restart openwebui > /dev/null 2>&1 && log "openwebui neu gestartet (Modellliste geändert)."
    fi
    alarm "Gateway aktualisiert ($SUMMARY). Neue Modelle/Aliasse: ${ADDED:-keine}. Entfallen: ${REMOVED:-keine}."
  else
    log "Modellliste unverändert."
    alarm "Gateway aktualisiert ($SUMMARY). Modellliste unverändert."
  fi

  local old_tags t
  old_tags=$(docker images "$IMAGE" --format '{{.Tag}}' | grep -E '^prev-.*-selfupdate$' | sort -r | tail -n +$((KEEP_PREV + 1)))
  for t in $old_tags; do docker rmi "$IMAGE:$t" > /dev/null 2>&1 && log "Altes Backup-Image $t entfernt."; done
  finish 0
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then main "$@"; fi
