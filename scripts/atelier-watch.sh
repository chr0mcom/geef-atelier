#!/usr/bin/env bash
# Wächter für Geef.Atelier: meldet hängende Läufe, Störungen des cli-proxy und ungesunde Container über Telegram.
#
# Gedacht für Cron alle 5 Minuten (Zeile und Betrieb: docs/ops/atelier-watch.md). Prüft:
#   runs            Läufe mit "Status" = 'Running', deren Start ("StartedAt", ersatzweise "CreatedAt") länger als
#                   STUCK_MINUTES (60) zurückliegt. Lesende Abfrage per `docker exec $PG_CONTAINER psql` mit
#                   default_transaction_read_only=on; Benutzer und Datenbank kommen aus der Umgebung des Containers.
#                   "Runs"."Status" ist eine Textspalte (EF speichert den Enum-Namen), deshalb der Vergleich mit 'Running'.
#   proxy           GET $GATEWAY/health des cli-proxy: nicht erreichbar, "status" != ok, "degraded": true oder ein
#                   Backend (ohne Reserve) mit offenem Failover-Breaker ("open_until" gesetzt) bzw. "healthy": false.
#   container-<n>   docker inspect je Container: läuft nicht oder Healthcheck != healthy.
#
# Drei Ergebnisse je Prüfung: ok, problem und unklar (Status `unknown`). Unklar heißt: Die Daten ließen sich nicht lesen
# (/health liefert HTTP 200, aber kein JSON-Objekt; die DB-Abfrage scheitert oder liefert nicht die Kennzeile `#watch-ok`;
# docker inspect scheitert aus einem anderen Grund als „no such object“ oder liefert etwas Unerwartetes). Unklar wird
# gemeldet und zählt als Problem (Exit 1), beendet aber NIE einen offenen Alarm: Nur ein eindeutiges ok schickt
# „Wieder in Ordnung“. Jeder Prüfpfad startet mit unklar, ein Absturz des Parsers kann also nie als ok durchgehen.
#
# Meldungen nur bei Zustandswechsel: Je Prüfung liegt eine Zustandsdatei in STATE_DIR (Status, Signatur, seit, letzte
# Meldung). Gemeldet wird, wenn ein Problem neu auftritt oder sich seine Signatur ändert (z. B. ein weiterer hängender
# Lauf, ein anderes gestörtes Backend, Wechsel zwischen problem und unklar), frühestens nach REMIND_SECONDS (6 Stunden)
# eine Erinnerung, und einmal „wieder in Ordnung“. Scheitert das Senden, bleibt der alte Zustand stehen und der nächste
# Lauf versucht es erneut.
#
# Zahlen aus der Umgebung (STUCK_MINUTES, REMIND_SECONDS, WATCH_NOW) und aus Zustandsdateien werden vor jeder Verwendung
# als ganze Zahl geprüft; ungültige Werte fallen mit Warnung im Log auf den Standard zurück und gelangen nie in SQL oder
# in eine Shell-Arithmetik.
#
# Aufruf:  scripts/atelier-watch.sh             (Cron, Log $STATE_DIR/watch.log)
#          DRY_RUN=1 scripts/atelier-watch.sh   prüfen, Meldungen nur ausgeben, nichts senden, keinen Zustand schreiben
# Exit-Code: 0 = alles in Ordnung, 1 = mindestens ein Problem (gemeldet oder schon bekannt), 2 = Lauf nicht möglich.
# Testbar: sourcebar (`main` nur bei direktem Aufruf). Per Umgebung überschreibbar: STATE_DIR, LOG, GATEWAY, PG_CONTAINER,
#          WATCH_CONTAINERS, STUCK_MINUTES, REMIND_SECONDS, WATCH_NOW (Epoche, nur für Tests), WATCH_PATH (PATH) und
#          ALARM_CMD (Standard wie beim Selfupdate: `hermes send --to telegram`, Text als letztes Argument).
#          Tests: cli-proxy/tests/test_atelier_watch_script.py (Attrappen für docker, curl und den Melder).
# Geheimnisse: Das Skript liest keine .env und gibt keine Zugangsdaten aus; der Melder holt seine Zugangsdaten selbst.

STATE_DIR=${STATE_DIR:-/srv/backup/atelier-watch}
LOG=${LOG:-$STATE_DIR/watch.log}
GATEWAY=${GATEWAY:-http://localhost:8090}
PG_CONTAINER=${PG_CONTAINER:-geef-atelier-postgres}
WATCH_CONTAINERS=${WATCH_CONTAINERS:-geef-atelier-web geef-atelier-cli-proxy}
STUCK_MINUTES=${STUCK_MINUTES:-60}
REMIND_SECONDS=${REMIND_SECONDS:-21600}
ALARM_CMD=${ALARM_CMD:-hermes send --to telegram}

PROBLEMS=0

log() { echo "[$(date '+%F %T')] $*"; }
now_epoch() { if [[ ${WATCH_NOW:-} =~ ^[0-9]{1,12}$ ]]; then echo "$WATCH_NOW"; else date +%s; fi; }

# Ganze Zahl > 0 mit höchstens sechs Stellen (verhindert Überlauf und jede Deutung als SQL oder Arithmetik-Ausdruck).
is_positive_int() { [[ $1 =~ ^[1-9][0-9]{0,5}$ ]]; }

validate_settings() {
  if ! is_positive_int "$STUCK_MINUTES"; then
    log "Warnung: STUCK_MINUTES='${STUCK_MINUTES:0:40}' ist keine positive ganze Zahl, nutze 60."
    STUCK_MINUTES=60
  fi
  if ! is_positive_int "$REMIND_SECONDS"; then
    log "Warnung: REMIND_SECONDS='${REMIND_SECONDS:0:40}' ist keine positive ganze Zahl, nutze 21600."
    REMIND_SECONDS=21600
  fi
}

send() {
  local text="Geef.Atelier-Wächter $(hostname -s) $(date '+%F %H:%M'): $*" cmd
  if [[ ${DRY_RUN:-0} == 1 ]]; then log "DRY_RUN, Meldung nicht gesendet: $text"; return 0; fi
  read -ra cmd <<< "$ALARM_CMD"
  if "${cmd[@]}" "$text" > /dev/null 2>&1; then log "Meldung gesendet: $*"; return 0; fi
  log "Meldung konnte nicht gesendet werden: $*"
  return 1
}

human_duration() {
  local s=$1
  if (( s >= 3600 )); then echo "$((s / 3600)) h $(((s % 3600) / 60)) min"; else echo "$((s / 60)) min"; fi
}

check_runs() {
  local sql out rc=0
  RESULT_STATUS=unknown; RESULT_SIG=unreadable; RESULT_DETAIL="Ergebnis der Läufe-Abfrage nicht lesbar"
  is_positive_int "$STUCK_MINUTES" || { RESULT_DETAIL="Schwellwert STUCK_MINUTES ungültig, Abfrage nicht ausgeführt"; return; }
  sql="select '#watch-ok' union all (select left(\"Id\"::text, 8) || ' ' || floor(extract(epoch from now() - coalesce(\"StartedAt\", \"CreatedAt\")) / 60)::int
       from \"Runs\" where \"Status\" = 'Running'
       and coalesce(\"StartedAt\", \"CreatedAt\") < now() - interval '$STUCK_MINUTES minutes'
       order by coalesce(\"StartedAt\", \"CreatedAt\"))"
  out=$(docker exec "$PG_CONTAINER" sh -c 'PGOPTIONS="-c default_transaction_read_only=on" psql -X -q -At -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "$1"' sh "$sql" 2>&1) || rc=$?
  if (( rc != 0 )); then
    RESULT_SIG=query-failed
    RESULT_DETAIL="Abfrage der Läufe fehlgeschlagen (Exit $rc): $(head -1 <<< "$out" | cut -c1-200)"
    return
  fi
  if ! grep -qx '#watch-ok' <<< "$out"; then
    RESULT_DETAIL="Ergebnis der Läufe-Abfrage nicht lesbar (Kennzeile fehlt): $(head -1 <<< "$out" | cut -c1-200)"
    return
  fi
  out=$(grep -E '^[0-9a-f]{8} [0-9]+$' <<< "$out")
  if [[ -z $out ]]; then RESULT_STATUS=ok; RESULT_SIG=""; RESULT_DETAIL="keine hängenden Läufe"; return; fi
  local n list
  n=$(wc -l <<< "$out")
  list=$(awk '{printf "%s%s (%s min)", (NR>1?", ":""), $1, $2}' <<< "$out")
  RESULT_STATUS=problem
  RESULT_SIG=$(awk '{print $1}' <<< "$out" | sort | tr '\n' ' ')
  RESULT_DETAIL="$n Lauf/Läufe länger als $STUCK_MINUTES min im Status Running: $list"
}

check_proxy() {
  local body rc=0 prc=0 verdict status sig detail
  RESULT_STATUS=unknown; RESULT_SIG=unreadable; RESULT_DETAIL="Gesundheitsdaten des cli-proxy nicht lesbar"
  body=$(curl -s -m 10 "$GATEWAY/health") || rc=$?
  if (( rc != 0 )); then
    RESULT_STATUS=problem; RESULT_SIG=unreachable; RESULT_DETAIL="cli-proxy /health nicht erreichbar (curl Exit $rc)"
    return
  fi
  if [[ -z ${body//[[:space:]]/} ]]; then RESULT_DETAIL+=": leere Antwort von /health"; return; fi
  verdict=$(python3 -c '
import json, sys

def clean(text):
    return " ".join(str(text).replace("|", "/").split())[:120]

def unknown(why):
    print("unknown|unreadable|Gesundheitsdaten des cli-proxy nicht lesbar: " + clean(why))
    sys.exit(0)

try:
    d = json.loads(sys.argv[1])
except Exception:
    unknown("Antwort von /health ist kein JSON")
if not isinstance(d, dict):
    unknown("Antwort von /health ist kein JSON-Objekt, sondern %s" % type(d).__name__)
failover = d.get("failover")
if failover is None:
    backends = {}
elif isinstance(failover, dict):
    backends = failover.get("backends") or {}
else:
    unknown("Feld failover ist kein Objekt")
if not isinstance(backends, dict):
    unknown("Feld failover.backends ist kein Objekt")
issues = []
if d.get("status") != "ok":
    issues.append("status=%s" % clean(d.get("status")))
if d.get("degraded") is True:
    issues.append("degraded")
for name, b in sorted(backends.items()):
    if not isinstance(b, dict) or b.get("reserve"):
        continue
    if b.get("open_until") is not None:
        issues.append("Breaker offen: %s" % clean(name))
    elif b.get("healthy") is False:
        issues.append("ungesund: %s" % clean(name))
if issues:
    print("problem|%s|cli-proxy gestört: %s" % (",".join(issues), ", ".join(issues)))
else:
    print("ok||cli-proxy gesund")
' "$body" 2>/dev/null) || prc=$?
  IFS='|' read -r status sig detail <<< "$verdict"
  case $status in
    ok|problem|unknown) (( prc == 0 )) && { RESULT_STATUS=$status; RESULT_SIG=$sig; RESULT_DETAIL=$detail; } ;;
  esac
  [[ $RESULT_STATUS == unknown && $RESULT_DETAIL != *": "* ]] && RESULT_DETAIL+=": Auswertung gescheitert (Exit $prc)"
  return 0
}

check_container() {
  local name=$1 out rc=0 state health extra
  RESULT_STATUS=unknown; RESULT_SIG=unreadable; RESULT_DETAIL="Zustand von Container $name nicht lesbar"
  out=$(docker inspect -f '{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$name" 2>&1) || rc=$?
  if (( rc != 0 )); then
    if grep -qiE 'no such (object|container)' <<< "$out"; then
      RESULT_STATUS=problem; RESULT_SIG=missing; RESULT_DETAIL="Container $name nicht gefunden"
    else
      RESULT_DETAIL+=" (docker inspect Exit $rc: $(head -1 <<< "$out" | cut -c1-160))"
    fi
    return
  fi
  read -r state health extra <<< "$out"
  if [[ ! $state =~ ^[a-z]+$ || ! $health =~ ^(healthy|unhealthy|starting|none)$ || -n $extra ]]; then
    RESULT_DETAIL+=" (unerwartete Ausgabe von docker inspect: $(head -1 <<< "$out" | cut -c1-80))"
    return
  fi
  if [[ $state != running ]]; then
    RESULT_STATUS=problem; RESULT_SIG="state-$state"; RESULT_DETAIL="Container $name läuft nicht (Status $state)"
  elif [[ $health != healthy && $health != none ]]; then
    RESULT_STATUS=problem; RESULT_SIG="health-$health"; RESULT_DETAIL="Container $name meldet Healthcheck $health"
  else
    RESULT_STATUS=ok; RESULT_SIG=""; RESULT_DETAIL="Container $name gesund"
  fi
}

# Vergleicht das Ergebnis einer Prüfung mit dem gespeicherten Zustand und meldet nur bei Wechsel oder fälliger Erinnerung.
# Offen ist ein Alarm, solange die Zustandsdatei problem oder unknown enthält; nur RESULT_STATUS=ok schließt ihn.
evaluate() {
  local key=$1 file="$STATE_DIR/$1.state" now prev_status=ok prev_sig="" since=0 last=0 status=$RESULT_STATUS
  now=$(now_epoch)
  if [[ -f $file ]]; then IFS='|' read -r prev_status prev_sig since last < "$file"; fi
  [[ $since =~ ^[0-9]{1,12}$ ]] || since=$now
  [[ $last =~ ^[0-9]{1,12}$ ]] || last=0
  [[ $status == ok || $status == problem ]] || status=unknown
  [[ $prev_status == ok || $prev_status == problem || $prev_status == unknown ]] || prev_status=unknown
  if [[ $status != ok ]]; then
    local label="Problem" still="gestört"
    [[ $status == unknown ]] && { label="Unklar"; still="unklar"; }
    PROBLEMS=$((PROBLEMS + 1))
    if [[ $prev_status == ok ]]; then
      log "$key: $label neu: $RESULT_DETAIL"
      send "$label ($key): $RESULT_DETAIL" && write_state "$file" "$status" "$RESULT_SIG" "$now" "$now"
    elif [[ $prev_status != "$status" || $prev_sig != "$RESULT_SIG" ]]; then
      local note=""
      [[ $status == unknown && $prev_status == problem ]] && note=", die gemeldete Störung gilt weiter als offen"
      log "$key: $label geändert: $RESULT_DETAIL$note"
      send "$label geändert ($key): $RESULT_DETAIL$note" && write_state "$file" "$status" "$RESULT_SIG" "$since" "$now"
    elif (( now - last >= REMIND_SECONDS )); then
      log "$key: Erinnerung: $RESULT_DETAIL"
      send "Weiterhin $still seit $(human_duration $((now - since))) ($key): $RESULT_DETAIL" \
        && write_state "$file" "$status" "$RESULT_SIG" "$since" "$now"
    else
      log "$key: weiterhin $still (bereits gemeldet): $RESULT_DETAIL"
    fi
  else
    if [[ $prev_status != ok ]]; then
      log "$key: wieder in Ordnung."
      send "Wieder in Ordnung ($key) nach $(human_duration $((now - since))): $RESULT_DETAIL" && clear_state "$file"
    else
      log "$key: ok."
    fi
  fi
}

write_state() {
  [[ ${DRY_RUN:-0} == 1 ]] && return 0
  local tmp
  tmp=$(mktemp "$STATE_DIR/.state.XXXXXX") && printf '%s|%s|%s|%s\n' "$2" "$3" "$4" "$5" > "$tmp" && mv "$tmp" "$1"
}

clear_state() {
  [[ ${DRY_RUN:-0} == 1 ]] && return 0
  rm -f "$1"
}

main() {
  set -uo pipefail
  umask 077
  export PATH=${WATCH_PATH:-/usr/local/bin:/usr/bin:/bin:/home/developer/.local/bin}

  mkdir -p -m 700 "$STATE_DIR" || { echo "STATE_DIR $STATE_DIR nicht anlegbar" >&2; exit 2; }
  exec 9> "$STATE_DIR/lock" || { echo "Sperrdatei $STATE_DIR/lock nicht anlegbar" >&2; exit 2; }
  if ! flock -n 9; then
    if [[ ${DRY_RUN:-0} == 1 ]]; then log "Anderer Lauf aktiv, Abbruch."; else log "Anderer Lauf aktiv, Abbruch." >> "$LOG" 2>&1; fi
    exit 0
  fi
  if [[ ${DRY_RUN:-0} != 1 ]]; then
    [[ -f $LOG && $(stat -c %s "$LOG") -gt 1048576 ]] && mv "$LOG" "$LOG.1"
    exec >> "$LOG" 2>&1
  fi
  validate_settings

  local name
  check_runs; evaluate runs
  check_proxy; evaluate proxy
  for name in $WATCH_CONTAINERS; do check_container "$name"; evaluate "container-$name"; done

  log "Fertig ($PROBLEMS Problem(e))."
  (( PROBLEMS == 0 )) || exit 1
  exit 0
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then main "$@"; fi
