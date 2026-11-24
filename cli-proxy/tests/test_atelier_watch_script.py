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
            cat "$FAKE_DIR/stuck_runs" 2>/dev/null
            ;;
          inspect)
            name=${@: -1}
            f="$FAKE_DIR/ctr_$name"
            [[ -f $f ]] || exit 1
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
