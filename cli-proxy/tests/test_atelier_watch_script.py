"""Tests für scripts/atelier-watch.sh (Betrieb: docs/ops/atelier-watch.md).

Das Skript läuft als echter bash-Prozess gegen Attrappen (docker, curl, Melder) in einem tmp-Verzeichnis. Kein echtes
Docker, keine echte Datenbank, keine echte Telegram-Meldung.
"""
import json
import os
import stat
import subprocess
import textwrap
from pathlib import Path

import pytest

SCRIPT = Path(os.environ.get("WATCH_SCRIPT") or Path(__file__).resolve().parents[2] / "scripts" / "atelier-watch.sh")

pytestmark = pytest.mark.skipif(not SCRIPT.exists(), reason="scripts/atelier-watch.sh fehlt (Test läuft nur auf dem Host)")

HEALTHY = {
    "status": "ok",
    "degraded": False,
    "failover": {
        "backends": {
            "claude": {"healthy": True, "open_until": None, "reserve": False},
            "codex": {"healthy": True, "open_until": None, "reserve": False},
            "claude-alt": {"healthy": False, "open_until": 123.0, "reserve": True},
        }
    },
}

FAKES = {
    "alarm": r"""
        #!/usr/bin/env bash
        [[ -n ${FAKE_ALARM_FAIL:-} ]] && exit 1
        printf '%s\n' "$*" >> "$FAKE_DIR/alarms.log"
    """,
    "docker": r"""
        #!/usr/bin/env bash
        printf '%s\n' "$*" >> "$FAKE_DIR/docker.argv"
        case $1 in
          exec)
            [[ -n ${FAKE_PSQL_FAIL:-} ]] && { echo "psql: error: connection refused" >&2; exit 2; }
            [[ -z ${FAKE_PSQL_NO_SENTINEL:-} ]] && echo '#watch-ok'
            cat "$FAKE_DIR/stuck_runs" 2>/dev/null
            ;;
          inspect)
            [[ -n ${FAKE_DOCKER_DOWN:-} ]] && { echo "Cannot connect to the Docker daemon at unix:///var/run/docker.sock" >&2; exit 1; }
            name=${@: -1}
            f="$FAKE_DIR/ctr_$name"
            [[ -f $f ]] || { echo "error: no such object: $name" >&2; exit 1; }
            cat "$f"
            ;;
        esac
        exit 0
    """,
    "curl": r"""
        #!/usr/bin/env bash
        printf '%s\n' "$*" >> "$FAKE_DIR/curl.argv"
        [[ -n ${FAKE_PROXY_DOWN:-} ]] && exit 7
        cat "$FAKE_DIR/health.json"
    """,
}


def _write_exec(path: Path, content: str) -> None:
    path.write_text(textwrap.dedent(content).lstrip("\n"))
    path.chmod(path.stat().st_mode | stat.S_IXUSR)


class Sandbox:
    def __init__(self, root: Path):
        self.root = root
        self.fake = root / "fake"
        self.bin = root / "bin"
        self.state = root / "state"
        self.fallback = root / "shm" / "atelier-watch"
        for d in (self.fake, self.bin):
            d.mkdir(parents=True)
        for name, body in FAKES.items():
            _write_exec(self.bin / name, body)
        self.set_health(HEALTHY)
        self.set_container("geef-atelier-web", "running healthy")
        self.set_container("geef-atelier-cli-proxy", "running healthy")
        self.now = 1_800_000_000

    def set_health(self, data) -> None:
        (self.fake / "health.json").write_text(data if isinstance(data, str) else json.dumps(data))

    def set_container(self, name: str, value: str | None) -> None:
        f = self.fake / f"ctr_{name}"
        if value is None:
            f.unlink(missing_ok=True)
        else:
            f.write_text(value + "\n")

    def set_stuck(self, *lines: str) -> None:
        (self.fake / "stuck_runs").write_text("".join(line + "\n" for line in lines))

    def run(self, advance: int = 300, **extra: str) -> subprocess.CompletedProcess:
        self.now += advance
        env = {
            "PATH": "/usr/bin:/bin",
            "WATCH_PATH": f"{self.bin}:/usr/bin:/bin",
            "FAKE_DIR": str(self.fake),
            "STATE_DIR": str(self.state),
            "WATCH_FALLBACK_DIR": str(self.fallback),
            "GATEWAY": "http://gw.test:8090",
            "ALARM_CMD": str(self.bin / "alarm"),
            "WATCH_NOW": str(self.now),
        }
        env.update(extra)
        return subprocess.run(["bash", str(SCRIPT)], env=env, capture_output=True, text=True, timeout=60, cwd=self.root)

    def read(self, name: str) -> str:
        p = self.fake / name
        return p.read_text() if p.exists() else ""

    @property
    def alarms(self) -> list[str]:
        return [line for line in self.read("alarms.log").splitlines() if line]

    @property
    def logtext(self) -> str:
        p = self.state / "watch.log"
        return p.read_text() if p.exists() else ""


@pytest.fixture
def sb(tmp_path):
    return Sandbox(tmp_path)


def test_all_healthy_sends_nothing_and_exits_zero(sb):
    r = sb.run()
    assert r.returncode == 0, sb.logtext
    assert sb.alarms == []
    assert "Fertig (0 Problem(e))." in sb.logtext


def test_runs_query_is_read_only_and_targets_running_status(sb):
    sb.run()
    argv = sb.read("docker.argv")
    assert "exec geef-atelier-postgres" in argv
    assert "default_transaction_read_only=on" in argv
    assert "\"Status\" = 'Running'" in argv
    assert "interval '60 minutes'" in argv


def test_stuck_run_alerts_once_then_stays_quiet(sb):
    sb.set_stuck("1a2b3c4d 75")
    r = sb.run()
    assert r.returncode == 1
    assert len(sb.alarms) == 1
    assert "Problem (runs)" in sb.alarms[0] and "1a2b3c4d (75 min)" in sb.alarms[0]
    for _ in range(3):
        sb.run()
    assert len(sb.alarms) == 1
    assert "bereits gemeldet" in sb.logtext


def test_reminder_after_six_hours(sb):
    sb.set_stuck("1a2b3c4d 75")
    sb.run()
    sb.run(advance=6 * 3600 - 600)
    assert len(sb.alarms) == 1
    sb.run(advance=600)
    assert len(sb.alarms) == 2
    assert "Weiterhin gestört seit 6 h 0 min (runs)" in sb.alarms[1]


def test_new_stuck_run_is_a_change(sb):
    sb.set_stuck("1a2b3c4d 75")
    sb.run()
    sb.set_stuck("1a2b3c4d 80", "99887766 61")
    sb.run()
    assert len(sb.alarms) == 2
    assert "Problem geändert (runs)" in sb.alarms[1] and "99887766" in sb.alarms[1]
    sb.set_stuck("1a2b3c4d 85", "99887766 66")
    sb.run()
    assert len(sb.alarms) == 2


def test_recovery_message_once(sb):
    sb.set_stuck("1a2b3c4d 75")
    sb.run()
    sb.set_stuck()
    r = sb.run(advance=1200)
    assert r.returncode == 0
    assert len(sb.alarms) == 2
    assert "Wieder in Ordnung (runs) nach 20 min" in sb.alarms[1]
    sb.run()
    assert len(sb.alarms) == 2
    assert not (sb.state / "runs.state").exists()


def test_query_failure_is_a_problem(sb):
    r = sb.run(FAKE_PSQL_FAIL="1")
    assert r.returncode == 1
    assert "Abfrage der Läufe fehlgeschlagen" in sb.alarms[0]


def test_query_noise_lines_are_ignored(sb):
    sb.set_stuck("", "SET", "kein treffer")
    r = sb.run()
    assert r.returncode == 0, sb.logtext
    assert sb.alarms == []


@pytest.mark.parametrize(
    ("mutate", "expected"),
    [
        (lambda h: h.update(degraded=True), "degraded"),
        (lambda h: h["failover"]["backends"]["codex"].update(open_until=1800000100.0), "Breaker offen: codex"),
        (lambda h: h["failover"]["backends"]["claude"].update(healthy=False), "ungesund: claude"),
        (lambda h: h.update(status="down"), "status=down"),
    ],
)
def test_proxy_problems(sb, mutate, expected):
    health = json.loads(json.dumps(HEALTHY))
    mutate(health)
    sb.set_health(health)
    r = sb.run()
    assert r.returncode == 1
    assert len(sb.alarms) == 1
    assert "Problem (proxy)" in sb.alarms[0] and expected in sb.alarms[0]


def test_reserve_backend_breaker_is_ignored(sb):
    r = sb.run()
    assert r.returncode == 0
    assert sb.alarms == []


def test_proxy_unreachable_and_invalid_json(sb):
    sb.run(FAKE_PROXY_DOWN="1")
    assert "nicht erreichbar" in sb.alarms[0]
    sb.set_health("<html>")
    sb.run()
    assert "kein JSON" in sb.alarms[1]


@pytest.mark.parametrize(
    ("value", "expected"),
    [("exited none", "läuft nicht (Status exited)"), ("running unhealthy", "Healthcheck unhealthy"), (None, "nicht gefunden")],
)
def test_container_problems(sb, value, expected):
    sb.set_container("geef-atelier-web", value)
    r = sb.run()
    assert r.returncode == 1
    assert len(sb.alarms) == 1
    assert "container-geef-atelier-web" in sb.alarms[0] and expected in sb.alarms[0]


def test_container_without_healthcheck_is_ok(sb):
    sb.set_container("geef-atelier-cli-proxy", "running none")
    assert sb.run().returncode == 0
    assert sb.alarms == []


def test_failed_send_keeps_state_and_retries(sb):
    sb.set_stuck("1a2b3c4d 75")
    sb.run(FAKE_ALARM_FAIL="1")
    assert sb.alarms == []
    assert not (sb.state / "runs.state").exists()
    assert "Meldung konnte nicht gesendet werden" in sb.logtext
    sb.run()
    assert len(sb.alarms) == 1 and "Problem (runs)" in sb.alarms[0]


def test_dry_run_prints_sends_nothing_and_writes_no_state(sb):
    sb.set_stuck("1a2b3c4d 75")
    r = sb.run(DRY_RUN="1")
    assert r.returncode == 1
    assert sb.alarms == []
    assert "DRY_RUN, Meldung nicht gesendet" in r.stdout and "1a2b3c4d" in r.stdout
    assert not (sb.state / "runs.state").exists()
    assert not (sb.state / "watch.log").exists()


def test_lock_is_respected(sb):
    sb.state.mkdir(exist_ok=True)
    holder = subprocess.Popen(["bash", "-c", f'exec 9> "{sb.state}/lock"; flock -n 9 && echo locked && sleep 20'], stdout=subprocess.PIPE, text=True)
    try:
        assert holder.stdout.readline().strip() == "locked"
        sb.set_stuck("1a2b3c4d 75")
        r = sb.run()
        assert r.returncode == 0
        assert "Anderer Lauf aktiv" in sb.logtext
        assert sb.alarms == []
    finally:
        holder.kill()
        holder.wait()


def test_script_is_sourceable_without_side_effects(tmp_path):
    r = subprocess.run(["bash", "-c", f'source "{SCRIPT}"; echo "$STATE_DIR $STUCK_MINUTES $REMIND_SECONDS"'],
                       capture_output=True, text=True, env={"PATH": "/usr/bin:/bin"}, cwd=tmp_path)
    assert r.returncode == 0
    assert r.stdout.strip() == "/srv/backup/atelier-watch 60 21600"


# Unklare Daten: eigener Zustand, Meldung, nie ein Ende des offenen Alarms (Q8)

UNREADABLE_HEALTH = [
    "[]",
    "null",
    "",
    "   \n",
    "<html><body>502 Bad Gateway</body></html>",
    "42",
    '"ok"',
    '{"status": "ok", "failover": []}',
    '{"status": "ok", "failover": {"backends": [1, 2]}}',
]


def _open_proxy_alarm(sb):
    health = json.loads(json.dumps(HEALTHY))
    health["degraded"] = True
    sb.set_health(health)
    sb.run()
    assert len(sb.alarms) == 1 and "Problem (proxy)" in sb.alarms[0]
    assert (sb.state / "proxy.state").read_text().startswith("problem|")


@pytest.mark.parametrize("body", UNREADABLE_HEALTH)
def test_unreadable_health_never_clears_open_alarm(sb, body):
    _open_proxy_alarm(sb)
    sb.set_health(body)
    r = sb.run()
    assert r.returncode == 1, sb.logtext
    assert not any("Wieder in Ordnung" in a for a in sb.alarms)
    assert "] proxy: ok" not in sb.logtext and "] proxy: wieder in Ordnung" not in sb.logtext
    assert "Traceback" not in sb.logtext
    assert len(sb.alarms) == 2
    assert "Unklar geändert (proxy): Gesundheitsdaten des cli-proxy nicht lesbar" in sb.alarms[1]
    assert "gilt weiter als offen" in sb.alarms[1]
    assert (sb.state / "proxy.state").read_text().startswith("unknown|")
    sb.run()
    assert len(sb.alarms) == 2
    assert "weiterhin unklar (bereits gemeldet)" in sb.logtext


@pytest.mark.parametrize("body", UNREADABLE_HEALTH)
def test_unreadable_health_from_ok_is_reported(sb, body):
    sb.set_health(body)
    r = sb.run()
    assert r.returncode == 1, sb.logtext
    assert len(sb.alarms) == 1
    assert sb.alarms[0].split(": ", 1)[1].startswith("Unklar (proxy): Gesundheitsdaten des cli-proxy nicht lesbar")
    assert "Fertig (1 Problem(e))." in sb.logtext


def test_unclear_then_healthy_sends_one_recovery(sb):
    sb.set_health("[]")
    sb.run()
    sb.set_health(HEALTHY)
    r = sb.run(advance=600)
    assert r.returncode == 0
    assert len(sb.alarms) == 2 and "Wieder in Ordnung (proxy) nach 10 min" in sb.alarms[1]
    assert not (sb.state / "proxy.state").exists()


def test_unclear_then_problem_is_a_change_and_keeps_since(sb):
    _open_proxy_alarm(sb)
    sb.set_health("[]")
    sb.run(advance=600)
    since = (sb.state / "proxy.state").read_text().split("|")[2]
    health = json.loads(json.dumps(HEALTHY))
    health["degraded"] = True
    sb.set_health(health)
    sb.run(advance=600)
    assert "Problem geändert (proxy)" in sb.alarms[2]
    assert (sb.state / "proxy.state").read_text().split("|")[2] == since


def test_crashing_health_parser_is_unclear_not_ok(sb):
    _open_proxy_alarm(sb)
    _write_exec(sb.bin / "python3", "#!/usr/bin/env bash\necho 'Traceback (most recent call last):' >&2\nexit 1\n")
    sb.set_health(HEALTHY)
    r = sb.run()
    assert r.returncode == 1
    assert not any("Wieder in Ordnung" in a for a in sb.alarms)
    assert "Auswertung gescheitert (Exit 1)" in sb.alarms[-1]


def _open_runs_alarm(sb):
    sb.set_stuck("1a2b3c4d 75")
    sb.run()
    assert "Problem (runs)" in sb.alarms[0]


@pytest.mark.parametrize("env", [{"FAKE_PSQL_FAIL": "1"}, {"FAKE_PSQL_NO_SENTINEL": "1"}])
def test_unreadable_runs_query_never_clears_open_alarm(sb, env):
    _open_runs_alarm(sb)
    sb.set_stuck()
    r = sb.run(**env)
    assert r.returncode == 1
    assert not any("Wieder in Ordnung" in a for a in sb.alarms)
    assert "Unklar geändert (runs)" in sb.alarms[1]
    assert (sb.state / "runs.state").exists()


def test_runs_query_without_sentinel_from_ok_is_unclear(sb):
    r = sb.run(FAKE_PSQL_NO_SENTINEL="1")
    assert r.returncode == 1
    assert "Unklar (runs): Ergebnis der Läufe-Abfrage nicht lesbar (Kennzeile fehlt)" in sb.alarms[0]


def test_runs_query_carries_sentinel(sb):
    sb.run()
    assert "select '#watch-ok' union all (select" in sb.read("docker.argv")


@pytest.mark.parametrize(
    ("env", "value", "expected"),
    [
        ({"FAKE_DOCKER_DOWN": "1"}, "running healthy", "Cannot connect to the Docker daemon"),
        ({}, "running", "unerwartete Ausgabe"),
        ({}, "<no value> <no value>", "unerwartete Ausgabe"),
        ({}, "running healthy extra", "unerwartete Ausgabe"),
    ],
)
def test_unreadable_container_state_never_clears_open_alarm(sb, env, value, expected):
    sb.set_container("geef-atelier-web", "exited none")
    sb.run()
    assert "Problem (container-geef-atelier-web)" in sb.alarms[0]
    sb.set_container("geef-atelier-web", value)
    r = sb.run(**env)
    assert r.returncode == 1
    assert not any("Wieder in Ordnung" in a for a in sb.alarms)
    assert any("Unklar geändert (container-geef-atelier-web)" in a and expected in a for a in sb.alarms)


# Schwellwert nie ungeprüft in SQL oder Arithmetik (Q14)

@pytest.mark.parametrize("value", ["60'", "abc", "0", "-5", "1 minutes'; select 1; --", " 60", "07", "9999999"])
def test_invalid_stuck_minutes_falls_back_with_warning(sb, value):
    r = sb.run(STUCK_MINUTES=value)
    assert r.returncode == 0, sb.logtext
    assert "Warnung: STUCK_MINUTES=" in sb.logtext and "nutze 60" in sb.logtext
    argv = sb.read("docker.argv")
    assert "interval '60 minutes'" in argv
    assert f"interval '{value} minutes'" not in argv


def test_empty_stuck_minutes_means_default(sb):
    sb.run(STUCK_MINUTES="")
    assert "interval '60 minutes'" in sb.read("docker.argv")


def test_valid_stuck_minutes_is_used(sb):
    sb.run(STUCK_MINUTES="90")
    assert "interval '90 minutes'" in sb.read("docker.argv")
    assert "Warnung" not in sb.logtext


def test_remind_seconds_is_never_evaluated_as_arithmetic(sb, tmp_path):
    marker = tmp_path / "pwned"
    sb.set_stuck("1a2b3c4d 75")
    sb.run()
    r = sb.run(REMIND_SECONDS=f"a[$(touch {marker})]", WATCH_NOW=f"b[$(touch {marker})]")
    assert r.returncode == 1
    assert not marker.exists()
    assert "Warnung: REMIND_SECONDS=" in sb.logtext


def test_corrupt_state_file_does_not_crash(sb):
    sb.state.mkdir()
    (sb.state / "runs.state").write_text("problem|x|$(boom)|y\n")
    (sb.state / "proxy.state").write_text("garbage\n")
    r = sb.run()
    assert "Fertig (" in sb.logtext, sb.logtext + r.stderr
    assert "syntax error" not in sb.logtext


# Log-Rotation nur durch den Sperrinhaber (Q21)

def _big_log(sb):
    sb.state.mkdir(exist_ok=True)
    (sb.state / "watch.log").write_text("x" * 1_100_000 + "\n")


def test_log_is_not_rotated_while_another_run_holds_the_lock(sb):
    _big_log(sb)
    holder = subprocess.Popen(["bash", "-c", f'exec 9> "{sb.state}/lock"; flock -n 9 && echo locked && sleep 20'], stdout=subprocess.PIPE, text=True)
    try:
        assert holder.stdout.readline().strip() == "locked"
        r = sb.run()
        assert r.returncode == 0
        assert not (sb.state / "watch.log.1").exists()
        assert sb.logtext.startswith("x" * 100)
        assert sb.logtext.rstrip().endswith("Anderer Lauf aktiv, Abbruch.")
    finally:
        holder.kill()
        holder.wait()


def test_lock_holder_rotates_big_log(sb):
    _big_log(sb)
    r = sb.run()
    assert r.returncode == 0
    assert (sb.state / "watch.log.1").exists()
    assert "Fertig (0 Problem(e))." in sb.logtext and not sb.logtext.startswith("x")


# Zustand nicht speicherbar: Schreibfehler loggen, zählen, einmal melden, nie eine Flut (N6)

FULL_DISK_MKTEMP = r"""
    #!/usr/bin/env bash
    tmpl=${@: -1}
    if [[ -n ${FAKE_FULL_DIR:-} && $tmpl == "$FAKE_FULL_DIR"/* ]]; then
      p=${tmpl%XXXXXX}$RANDOM$RANDOM
      ln -s /dev/full "$p" || exit 1
      echo "$p"
      exit 0
    fi
    exec /usr/bin/mktemp "$@"
"""

needs_non_root = pytest.mark.skipif(hasattr(os, "geteuid") and os.geteuid() == 0, reason="root ignoriert Dateirechte")


def _alarms_for(sb, key):
    return [a for a in sb.alarms if f"({key})" in a]


def _assert_no_temp_leftovers(directory: Path):
    assert not [p.name for p in directory.iterdir() if p.name.startswith((".state.", ".probe."))]


@pytest.fixture
def read_only_state(sb):
    sb.run()
    sb.state.chmod(0o500)
    yield sb
    sb.state.chmod(0o700)


@needs_non_root
def test_unwritable_state_dir_reports_once_and_keeps_state_in_fallback(read_only_state):
    sb = read_only_state
    sb.set_stuck("1a2b3c4d 75")
    r = sb.run()
    assert r.returncode == 1
    assert len(_alarms_for(sb, "runs")) == 1
    state_alarms = _alarms_for(sb, "state-dir")
    assert len(state_alarms) == 1 and "nicht beschreibbar" in state_alarms[0] and str(sb.fallback) in state_alarms[0]
    assert "Schreibfehler: Zustandsverzeichnis" in sb.logtext and "Schreibfehler)." in sb.logtext
    assert (sb.fallback / "runs.state").read_text().startswith("problem|")
    for _ in range(5):
        sb.run()
    assert len(sb.alarms) == 2, sb.alarms
    assert "runs: weiterhin gestört (bereits gemeldet)" in sb.logtext


@needs_non_root
def test_fallback_state_returns_to_state_dir_when_writable_again(read_only_state):
    sb = read_only_state
    sb.set_stuck("1a2b3c4d 75")
    sb.run()
    sb.state.chmod(0o700)
    r = sb.run()
    assert r.returncode == 1
    assert (sb.state / "runs.state").read_text().startswith("problem|")
    assert not (sb.fallback / "runs.state").exists()
    assert len(_alarms_for(sb, "runs")) == 1
    assert "Wieder in Ordnung (state-dir)" in sb.alarms[-1]
    assert "Zustand aus der Ersatzablage zurückgeführt" in sb.logtext
    sb.run()
    assert len(sb.alarms) == 3


@needs_non_root
def test_recovery_while_state_dir_is_unwritable_is_sent_once(sb):
    sb.set_stuck("1a2b3c4d 75")
    sb.run()
    sb.state.chmod(0o500)
    try:
        sb.set_stuck()
        sb.run()
        sb.run()
        sb.run()
    finally:
        sb.state.chmod(0o700)
    assert len([a for a in sb.alarms if "Wieder in Ordnung (runs)" in a]) == 1
    assert "nicht löschbar" in sb.logtext
    sb.run()
    assert not (sb.state / "runs.state").exists()
    assert len([a for a in sb.alarms if "Wieder in Ordnung (runs)" in a]) == 1


def test_full_disk_in_state_dir_uses_fallback_without_flood(sb):
    _write_exec(sb.bin / "mktemp", FULL_DISK_MKTEMP)
    sb.set_stuck("1a2b3c4d 75")
    for _ in range(6):
        r = sb.run(FAKE_FULL_DIR=str(sb.state))
        assert r.returncode == 1
    assert len(_alarms_for(sb, "runs")) == 1
    assert len(_alarms_for(sb, "state-dir")) == 1
    assert (sb.fallback / "runs.state").exists()
    assert "Schreibfehler" in sb.logtext
    _assert_no_temp_leftovers(sb.state)


def test_no_writable_place_at_all_sends_at_most_once_per_window(sb):
    _write_exec(sb.bin / "mktemp", FULL_DISK_MKTEMP)
    sb.set_stuck("1a2b3c4d 75")
    for _ in range(24):
        assert sb.run(FAKE_FULL_DIR=str(sb.root), REMIND_SECONDS="3600").returncode == 1
    runs, state = _alarms_for(sb, "runs"), _alarms_for(sb, "state-dir")
    assert len(runs) == 2 and len(state) == 2, sb.alarms
    assert "keine Ersatzablage" in state[0]
    assert "Meldung gedrosselt" in sb.logtext
    _assert_no_temp_leftovers(sb.state)
    _assert_no_temp_leftovers(sb.fallback)


def test_symlinked_fallback_dir_is_never_used(sb, tmp_path):
    _write_exec(sb.bin / "mktemp", FULL_DISK_MKTEMP)
    target = tmp_path / "fremd"
    target.mkdir(mode=0o700)
    sb.fallback.parent.mkdir(parents=True)
    sb.fallback.symlink_to(target)
    sb.set_stuck("1a2b3c4d 75")
    sb.run(FAKE_FULL_DIR=str(sb.state))
    assert list(target.iterdir()) == []
    assert "symbolischer Link" in sb.logtext


def test_invalid_watch_interval_falls_back(sb):
    r = sb.run(WATCH_INTERVAL="5; rm")
    assert r.returncode == 0, sb.logtext
    assert "Warnung: WATCH_INTERVAL=" in sb.logtext and "nutze 300" in sb.logtext


def test_healthy_run_does_not_create_fallback_dir(sb):
    sb.run()
    assert not sb.fallback.exists()
    assert "Schreibfehler" not in sb.logtext
