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
# Meldungen nur bei Zustandswechsel: Je Prüfung liegt eine Zustandsdatei in STATE_DIR (Status, Signatur, seit, letzte
# Meldung). Gemeldet wird, wenn ein Problem neu auftritt oder sich seine Signatur ändert (z. B. ein weiterer hängender
# Lauf, ein anderes gestörtes Backend), frühestens nach REMIND_SECONDS (6 Stunden) eine Erinnerung, und einmal
# „wieder in Ordnung“. Scheitert das Senden, bleibt der alte Zustand stehen und der nächste Lauf versucht es erneut.
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
now_epoch() { echo "${WATCH_NOW:-$(date +%s)}"; }

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
  sql="select left(\"Id\"::text, 8) || ' ' || floor(extract(epoch from now() - coalesce(\"StartedAt\", \"CreatedAt\")) / 60)::int
       from \"Runs\" where \"Status\" = 'Running'
       and coalesce(\"StartedAt\", \"CreatedAt\") < now() - interval '$STUCK_MINUTES minutes'
       order by coalesce(\"StartedAt\", \"CreatedAt\")"
  out=$(docker exec "$PG_CONTAINER" sh -c 'PGOPTIONS="-c default_transaction_read_only=on" psql -X -q -At -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "$1"' sh "$sql" 2>&1) || rc=$?
  if (( rc != 0 )); then
    RESULT_STATUS=problem; RESULT_SIG=query-failed
    RESULT_DETAIL="Abfrage der Läufe fehlgeschlagen (Exit $rc): $(head -1 <<< "$out" | cut -c1-200)"
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
  local body rc=0 verdict
  body=$(curl -s -m 10 "$GATEWAY/health") || rc=$?
  if (( rc != 0 )) || [[ -z $body ]]; then
    RESULT_STATUS=problem; RESULT_SIG=unreachable; RESULT_DETAIL="cli-proxy /health nicht erreichbar (curl Exit $rc)"
    return
  fi
  verdict=$(python3 -c '
import json, sys
try:
    d = json.loads(sys.argv[1])
except Exception:
    print("problem|invalid|Antwort von /health ist kein JSON"); sys.exit()
issues = []
if d.get("status") != "ok":
    issues.append("status=%s" % d.get("status"))
if d.get("degraded") is True:
    issues.append("degraded")
for name, b in sorted(((d.get("failover") or {}).get("backends") or {}).items()):
    if not isinstance(b, dict) or b.get("reserve"):
        continue
    if b.get("open_until") is not None:
        issues.append("Breaker offen: %s" % name)
    elif b.get("healthy") is False:
        issues.append("ungesund: %s" % name)
if issues:
    print("problem|%s|cli-proxy gestört: %s" % (",".join(issues), ", ".join(issues)))
else:
    print("ok||cli-proxy gesund")
' "$body")
  IFS='|' read -r RESULT_STATUS RESULT_SIG RESULT_DETAIL <<< "$verdict"
}

check_container() {
  local name=$1 out rc=0 state health
  out=$(docker inspect -f '{{.State.Status}} {{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$name" 2>/dev/null) || rc=$?
  if (( rc != 0 )) || [[ -z $out ]]; then
    RESULT_STATUS=problem; RESULT_SIG=missing; RESULT_DETAIL="Container $name nicht gefunden"
    return
  fi
  read -r state health <<< "$out"
  if [[ $state != running ]]; then
    RESULT_STATUS=problem; RESULT_SIG="state-$state"; RESULT_DETAIL="Container $name läuft nicht (Status $state)"
  elif [[ $health != healthy && $health != none ]]; then
    RESULT_STATUS=problem; RESULT_SIG="health-$health"; RESULT_DETAIL="Container $name meldet Healthcheck $health"
  else
    RESULT_STATUS=ok; RESULT_SIG=""; RESULT_DETAIL="Container $name gesund"
  fi
}

# Vergleicht das Ergebnis einer Prüfung mit dem gespeicherten Zustand und meldet nur bei Wechsel oder fälliger Erinnerung.
evaluate() {
  local key=$1 file="$STATE_DIR/$1.state" now prev_status=ok prev_sig="" since=0 last=0
  now=$(now_epoch)
  if [[ -f $file ]]; then IFS='|' read -r prev_status prev_sig since last < "$file"; fi
  if [[ $RESULT_STATUS == problem ]]; then
    PROBLEMS=$((PROBLEMS + 1))
    if [[ $prev_status != problem ]]; then
      log "$key: Problem neu: $RESULT_DETAIL"
      send "Problem ($key): $RESULT_DETAIL" && write_state "$file" problem "$RESULT_SIG" "$now" "$now"
    elif [[ $prev_sig != "$RESULT_SIG" ]]; then
      log "$key: Problem geändert: $RESULT_DETAIL"
      send "Problem geändert ($key): $RESULT_DETAIL" && write_state "$file" problem "$RESULT_SIG" "$since" "$now"
    elif (( now - last >= REMIND_SECONDS )); then
      log "$key: Erinnerung: $RESULT_DETAIL"
      send "Weiterhin gestört seit $(human_duration $((now - since))) ($key): $RESULT_DETAIL" \
        && write_state "$file" problem "$RESULT_SIG" "$since" "$now"
    else
      log "$key: weiterhin gestört (bereits gemeldet): $RESULT_DETAIL"
    fi
  else
    if [[ $prev_status == problem ]]; then
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
  if [[ ${DRY_RUN:-0} != 1 ]]; then
    [[ -f $LOG && $(stat -c %s "$LOG") -gt 1048576 ]] && mv "$LOG" "$LOG.1"
    exec >> "$LOG" 2>&1
  fi

  exec 9> "$STATE_DIR/lock"
  if ! flock -n 9; then log "Anderer Lauf aktiv, Abbruch."; exit 0; fi

  local name
  check_runs; evaluate runs
  check_proxy; evaluate proxy
  for name in $WATCH_CONTAINERS; do check_container "$name"; evaluate "container-$name"; done

  log "Fertig ($PROBLEMS Problem(e))."
  (( PROBLEMS == 0 )) || exit 1
  exit 0
}

if [[ ${BASH_SOURCE[0]} == "$0" ]]; then main "$@"; fi
