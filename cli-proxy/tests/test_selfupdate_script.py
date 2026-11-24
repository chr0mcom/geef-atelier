"""Tests für scripts/cli-proxy-selfupdate.sh (Vertrag: geef_architecture.md §8.4).

Das Skript läuft als echter bash-Prozess gegen Fake-CLIs (docker, npm, curl, codex, claude, sleep) in einem
tmp-Verzeichnis. Kein echtes Docker, kein echtes npm, keine echte Telegram-Meldung, keine echten Dateien.
"""
import json
import os
import shutil
import stat
import subprocess
import textwrap
import tomllib
from pathlib import Path

import pytest

SCRIPT = Path(os.environ.get("SELFUPDATE_SCRIPT") or Path(__file__).resolve().parents[2] / "scripts" / "cli-proxy-selfupdate.sh")
KEY = "sk-test-SECRET-gateway-key-0123456789"

pytestmark = pytest.mark.skipif(not SCRIPT.exists(), reason="scripts/cli-proxy-selfupdate.sh fehlt (Test läuft nur auf dem Host)")

FAKES = {
    "alarm": r"""
        #!/usr/bin/env bash
        printf '%s\n' "$*" >> "$FAKE_DIR/alarms.log"
    """,
    "git": r"""
        #!/usr/bin/env bash
        echo "$*" >> "$FAKE_DIR/git.log"
        [[ -n ${FAKE_GIT_FAIL:-} ]] && { echo "fatal: not a git repository" >&2; exit 128; }
        sub=$3
        calls=$(grep -c -- "-C [^ ]* $sub " "$FAKE_DIR/git.log")
        case $sub in
          status)
            [[ -n ${FAKE_GIT_DIRTY:-} ]] && printf '%s\n' $FAKE_GIT_DIRTY
            if [[ -f $FAKE_DIR/git_dirty ]] || [[ -n ${FAKE_GIT_DIRTY_FROM_CALL:-} ]] && (( calls >= ${FAKE_GIT_DIRTY_FROM_CALL:-1} )); then
              echo " M cli-proxy/src/main.py"
            fi
            ;;
          ls-files)
            [[ -n ${FAKE_GIT_LSFILES_FAIL:-} ]] && { echo "fatal: ls-files kaputt" >&2; exit 128; }
            [[ -n ${FAKE_GIT_IGNORED:-} ]] && printf '%s\n' $FAKE_GIT_IGNORED
            ;;
          rev-parse)
            [[ -n ${FAKE_GIT_NO_TREE:-} ]] && exit 1
            cat "$FAKE_DIR/git_tree" 2>/dev/null || echo 1111111111111111111111111111111111111111
            ;;
        esac
        exit 0
    """,
    "sleep": r"""
        #!/usr/bin/env bash
        [[ -n ${FAKE_DIRTY_DURING_WAIT:-} ]] && : > "$FAKE_DIR/git_dirty"
        exit 0
    """,
    "cp": r"""
        #!/usr/bin/env bash
        [[ -n ${FAKE_CP_FAIL:-} ]] && { echo "cp: Fake-Fehler" >&2; exit 1; }
        exec /usr/bin/cp "$@"
    """,
    "mv": r"""
        #!/usr/bin/env bash
        [[ -n ${FAKE_MV_FAIL:-} ]] && { echo "mv: Fake-Fehler" >&2; exit 1; }
        exec /usr/bin/mv "$@"
    """,
    "npm": r"""
        #!/usr/bin/env bash
        echo "$*" >> "$FAKE_DIR/npm.log"
        if [[ $1 == view ]]; then
          [[ -n ${FAKE_NPM_VIEW_EMPTY:-} ]] && exit 0
          case $2 in
            @anthropic-ai/claude-code) echo "${FAKE_NPM_CLAUDE:-2.1.322}";;
            @openai/codex) echo "${FAKE_NPM_CODEX:-0.171.0}";;
            @google/gemini-cli) echo "${FAKE_NPM_GEMINI:-0.5.0}";;
          esac
          exit 0
        fi
        if [[ $1 == install ]]; then
          prefix=""; spec=""
          shift
          while (($#)); do case $1 in --prefix) prefix=$2; shift 2;; -g) shift;; *) spec=$1; shift;; esac; done
          ver=${spec##*@}
          case $spec in @anthropic-ai/claude-code@*) cli=claude;; @openai/codex@*) cli=codex;; *) cli=gemini;; esac
          for bad in ${FAKE_NPM_FAIL_VERSIONS:-}; do [[ $bad == "$ver" ]] && { echo "npm ERR! fake" >&2; exit 1; }; done
          [[ -n ${FAKE_NPM_NOOP:-} ]] && exit 0
          for noop in ${FAKE_NPM_NOOP_VERSIONS:-}; do [[ $noop == "$ver" ]] && exit 0; done
          echo "$ver" > "$prefix/ver_$cli"
          exit 0
        fi
        exit 0
    """,
    "docker": r"""
        #!/usr/bin/env bash
        echo "$*" >> "$FAKE_DIR/docker.log"
        ref_file() { echo "$FAKE_DIR/img_$(tr '/:' '__' <<< "$1")"; }
        resolve_ref() {
          local f; f=$(ref_file "$1")
          if [[ -f $f ]]; then cat "$f"
          elif [[ $1 == *:latest ]]; then echo "sha256:${FAKE_TAG_BEFORE:-aaa}"
          elif [[ $1 == sha256:* ]]; then echo "$1"
          else return 1; fi
        }
        case $1 in
          exec)
            shift 2
            if [[ $1 == sh ]]; then
              if [[ -n ${FAKE_BUSY_CALLS:-} ]]; then
                n=$(( $(cat "$FAKE_DIR/busy_calls" 2>/dev/null || echo 0) + 1 )); echo "$n" > "$FAKE_DIR/busy_calls"
                if (( n <= FAKE_BUSY_CALLS )); then echo 1; else echo 0; fi
                exit 0
              fi
              echo "${FAKE_BUSY:-0}"; exit 0
            fi
            v=$(cat "$FAKE_DIR/ctr_$1" 2>/dev/null) || exit 1
            echo "$1 $v"
            ;;
          tag)
            [[ -n ${FAKE_TAG_FAIL:-} ]] && exit 1
            [[ -n ${FAKE_ROLLBACK_TAG_FAIL:-} && $3 == *:latest ]] && exit 1
            id=$(resolve_ref "$2") || exit 1
            echo "$id" > "$(ref_file "$3")"
            ;;
          inspect)
            if [[ -f $FAKE_DIR/ctr_image ]]; then cat "$FAKE_DIR/ctr_image"
            elif [[ -n ${FAKE_RUNNING_UNKNOWN:-} ]]; then exit 1
            else echo "sha256:${FAKE_RUNNING_BEFORE:-aaa}"; fi
            ;;
          image) [[ $2 == inspect ]] && { resolve_ref "${@: -1}" || exit 1; } ;;
          compose)
            case $2 in
              build)
                [[ -n ${FAKE_BUILD_FAIL:-} ]] && exit 1
                [[ -n ${FAKE_DIRTY_DURING_BUILD:-} ]] && : > "$FAKE_DIR/git_dirty"
                [[ -n ${FAKE_TREE_DURING_BUILD:-} ]] && echo "$FAKE_TREE_DURING_BUILD" > "$FAKE_DIR/git_tree"
                echo "sha256:new-build" > "$(ref_file geef-atelier-cli-proxy:latest)"
                ;;
              up)
                [[ -n ${FAKE_UP_FAIL:-} && $* != *--no-build* ]] && exit 1
                if [[ $* == *--no-build* ]]; then
                  : > "$FAKE_DIR/rolled_back"
                  [[ -n ${FAKE_ROLLBACK_UP_FAIL:-} ]] && exit 1
                  if [[ -n ${FAKE_CTR_IMAGE_ID:-} ]]; then echo "sha256:$FAKE_CTR_IMAGE_ID" > "$FAKE_DIR/ctr_image"
                  else resolve_ref geef-atelier-cli-proxy:latest > "$FAKE_DIR/ctr_image"; fi
                else
                  echo "sha256:new-build" > "$FAKE_DIR/ctr_image"
                fi
                for b in claude codex; do [[ -f $FAKE_DIR/models_$b.after.json ]] && /usr/bin/cp "$FAKE_DIR/models_$b.after.json" "$FAKE_DIR/models_$b.json"; done
                ;;
            esac
            ;;
          images) printf 'prev-20260101-000000-selfupdate\nprev-20260102-000000-selfupdate\nprev-20260103-000000-selfupdate\nprev-20260104-000000-selfupdate\nlatest\n' ;;
          ps) [[ -n ${FAKE_OPENWEBUI:-} ]] && echo openwebui ;;
        esac
        exit 0
    """,
    "curl": r"""
        #!/usr/bin/env bash
        printf '%s\n' "$*" >> "$FAKE_DIR/curl.argv"
        hdr=""; body=""; auth_file=""; url=""
        while (($#)); do
          case $1 in
            -D) hdr=$2; shift 2;;
            -o) body=$2; shift 2;;
            -H) [[ $2 == @* ]] && auth_file=${2#@}; shift 2;;
            -m|-X|-d) shift 2;;
            http*) url=$1; shift;;
            *) shift;;
          esac
        done
        if [[ -n $auth_file ]]; then
          if grep -q "Bearer $FAKE_KEY" "$auth_file"; then echo auth-file-ok >> "$FAKE_DIR/curl.auth"; else echo auth-file-bad >> "$FAKE_DIR/curl.auth"; fi
        fi
        out() { if [[ -n $body ]]; then cat > "$body"; else cat; fi; }
        case $url in
          */health)
            if [[ -n ${FAKE_UNHEALTHY_AFTER_ROLLBACK:-} && -f $FAKE_DIR/rolled_back ]]; then echo '{"status":"down"}' | out
            else echo '{"status":"ok","claude":"ready","codex":"ready"}' | out; fi
            ;;
          */v1/claude/models) cat "$FAKE_DIR/models_claude.json" | out ;;
          */v1/codex/models) cat "$FAKE_DIR/models_codex.json" | out ;;
          */chat/completions)
            backend=$(sed -E 's#.*/v1/([a-z]+)/chat.*#\1#' <<< "$url")
            code=200; content=${FAKE_SMOKE_CONTENT-pong}
            if [[ ${FAKE_SMOKE_FAIL:-} == *$backend* ]]; then code=500; content=""; fi
            provider=${FAKE_SMOKE_PROVIDER:-$backend}
            if [[ -n $hdr ]]; then
              printf 'HTTP/1.1 %s X\r\n' "$code" > "$hdr"
              [[ -n ${FAKE_SMOKE_NO_PROVIDER:-} ]] || printf 'x-cli-proxy-provider: %s\r\n' "$provider" >> "$hdr"
              [[ -n ${FAKE_SMOKE_FAILOVER:-} ]] && printf 'x-cli-proxy-failover: %s\r\n' "$FAKE_SMOKE_FAILOVER" >> "$hdr"
              printf '\r\n' >> "$hdr"
            fi
            printf '{"choices":[{"message":{"content":"%s"}}]}' "$content" | out
            exit "${FAKE_SMOKE_CURL_RC:-0}"
            ;;
        esac
        exit 0
    """,
}

CLI_FAKE = r"""
    #!/usr/bin/env bash
    here=$(cd "$(dirname "$0")/.." && pwd)
    name=$(basename "$0")
    ver=$(cat "$here/ver_$name")
    if [[ ${1:-} == --version ]]; then
      [[ $name == codex ]] && echo "codex-cli $ver" || echo "$ver (Claude Code)"
      exit 0
    fi
    prompt=${@: -1}
    mode=${FAKE_SMOKE_MODE:-ok}
    [[ -n ${FAKE_BROKEN_VERSION:-} && $ver == "$FAKE_BROKEN_VERSION" ]] && mode=prompt_echo_exit1
    if [[ $name == codex ]]; then
      cfgmodel=$(grep -E '^model *= *"' "$HOST_CODEX_CONFIG" | head -1 | sed -E 's/^model *= *"([^"]*)".*/\1/')
      model=$cfgmodel
      explicit=no
      out=""
      args=("$@")
      for ((i = 0; i < ${#args[@]}; i++)); do
        case ${args[$i]} in -o|--output-last-message) out=${args[$((i + 1))]};; -m|--model) model=${args[$((i + 1))]}; explicit=yes;; esac
      done
      [[ -n ${FAKE_CODEX_REJECT_IMPLICIT:-} && $explicit == no && $model == "$FAKE_CODEX_REJECT_IMPLICIT" ]] && mode=prompt_echo_exit1
      echo "$ver model=$model cfg=$cfgmodel" >> "$FAKE_DIR/codex_calls.log"
      [[ -n ${FAKE_CODEX_CACHE_REFRESH:-} && $ver == "$FAKE_CODEX_CACHE_REFRESH_VERSION" ]] && echo "$FAKE_CODEX_CACHE_REFRESH" > "$HOST_CODEX_MODELS"
      if [[ -n ${FAKE_CODEX_ACCEPT:-} ]]; then
        accepted=no
        for pair in $FAKE_CODEX_ACCEPT; do [[ $pair == "$ver:$model" ]] && accepted=yes; done
        [[ $accepted == no ]] && mode=prompt_echo_exit1
      fi
      [[ -n ${FAKE_REJECT_MODEL:-} && $model == "$FAKE_REJECT_MODEL" ]] && mode=prompt_echo_exit1
      [[ -n ${FAKE_SABOTAGE_BACKUP:-} && $mode == prompt_echo_exit1 ]] && rm -f "$HOST_CODEX_CONFIG".bak-selfupdate-*
      echo "OpenAI Codex v$ver"; echo "model: $model"; echo "user"; echo "$prompt"
      case $mode in
        ok) echo codex; echo "Pong."; [[ -n $out ]] && echo "Pong." > "$out"; exit 0;;
        prompt_echo_exit1) echo "ERROR: The '$model' model is not supported when using Codex with a ChatGPT account."; exit 1;;
        exit1_with_pong) [[ -n $out ]] && echo pong > "$out"; exit 1;;
        exit0_other_answer) echo codex; echo "Hallo"; [[ -n $out ]] && echo "Hallo" > "$out"; exit 0;;
        exit0_no_last_file) echo codex; echo "(nur der Prompt enthält pong)"; exit 0;;
      esac
    else
      case $mode in
        ok) echo "pong"; exit 0;;
        prompt_echo_exit1) echo "pong" ; echo "API Error" >&2; exit 1;;
        exit1_with_pong) echo pong; exit 1;;
        exit0_other_answer) echo "Hallo"; exit 0;;
        *) echo "Ich soll pong sagen"; exit 0;;
      esac
    fi
"""

DEFAULT_CONFIG = textwrap.dedent(
    '''\
    model = "gpt-6.1-sol"
    model_reasoning_effort = "medium"

    [projects."/srv/x"]
    trust_level = "trusted"
    '''
)

MODELS_CACHE = {
    "models": [
        {"slug": "gpt-6.2-sol", "visibility": "list"},
        {"slug": "gpt-6.1-sol", "visibility": "list"},
        {"slug": "gpt-6-luna", "visibility": "list"},
        {"slug": "gpt-7-sol", "visibility": "hide"},
    ]
}


def _write_exec(path: Path, content: str) -> None:
    path.write_text(textwrap.dedent(content).lstrip("\n"))
    path.chmod(path.stat().st_mode | stat.S_IXUSR)


class Sandbox:
    def __init__(self, root: Path):
        self.root = root
        self.fake = root / "fake"
        self.bin = root / "bin"
        self.prefix = root / "npm-global"
        self.state = root / "state"
        self.project = root / "project"
        self.home = root / "home"
        self.config = root / "codex" / "config.toml"
        self.models = root / "codex" / "models_cache.json"
        self.log = self.state / "update.log"
        for d in (self.fake, self.bin, self.prefix / "bin", self.project, self.home, self.config.parent):
            d.mkdir(parents=True)
        for name, body in FAKES.items():
            _write_exec(self.bin / name, body)
        for cli in ("codex", "claude"):
            _write_exec(self.prefix / "bin" / cli, CLI_FAKE)
        self.set_container(claude="2.1.322", codex="0.171.0", gemini="0.5.0")
        self.set_host(claude="2.1.322", codex="0.171.0")
        self.config.write_text(DEFAULT_CONFIG)
        self.models.write_text(json.dumps(MODELS_CACHE))
        (self.project / ".env").write_text(f"OTHER=1\nCLI_PROXY_API_KEY={KEY}\n")
        (self.fake / "models_claude.json").write_text(json.dumps({"data": [{"id": "claude-haiku-latest"}], "x_aliases": {"fable": "claude-fable-5-1"}}))
        (self.fake / "models_codex.json").write_text(json.dumps({"data": [{"id": "gpt-6.2-sol"}, {"id": "gpt-6.2-astra"}]}))
        self.env: dict[str, str] = {}

    def set_container(self, **versions: str) -> None:
        for cli, v in versions.items():
            (self.fake / f"ctr_{cli}").write_text(v)

    def set_host(self, **versions: str) -> None:
        for cli, v in versions.items():
            (self.prefix / f"ver_{cli}").write_text(v)

    def host_version(self, cli: str) -> str:
        return (self.prefix / f"ver_{cli}").read_text().strip()

    def base_env(self) -> dict[str, str]:
        env = {
            "PATH": "/usr/bin:/bin",
            "HOME": str(self.home),
            "SELFUPDATE_PATH": f"{self.bin}:/usr/bin:/bin",
            "FAKE_DIR": str(self.fake),
            "FAKE_KEY": KEY,
            "PROJECT_DIR": str(self.project),
            "CONTAINER": "fake-ctr",
            "STATE_DIR": str(self.state),
            "LOG": str(self.log),
            "GATEWAY": "http://gw.test:8090",
            "HOST_NPM_PREFIX": str(self.prefix),
            "HOST_CODEX_CONFIG": str(self.config),
            "HOST_CODEX_MODELS": str(self.models),
            "ALARM_CMD": str(self.bin / "alarm"),
            "WAIT_QUIET_SECONDS": "0",
        }
        env.update(self.env)
        return env

    def run(self, **extra: str) -> subprocess.CompletedProcess:
        env = self.base_env()
        env.update(extra)
        return subprocess.run(["bash", str(SCRIPT)], env=env, capture_output=True, text=True, timeout=90, cwd=self.root)

    def read(self, name: str) -> str:
        p = self.fake / name
        return p.read_text() if p.exists() else ""

    @property
    def alarms(self) -> str:
        return self.read("alarms.log")

    @property
    def logtext(self) -> str:
        return self.log.read_text() if self.log.exists() else ""

    @property
    def docker_log(self) -> str:
        return self.read("docker.log")

    def config_model(self) -> str | None:
        return tomllib.loads(self.config.read_text()).get("model")

    def leftovers(self) -> list[str]:
        return [p.name for p in self.config.parent.iterdir() if ".tmp-" in p.name]


@pytest.fixture
def sb(tmp_path):
    return Sandbox(tmp_path)


# 1. Smoke-Test: Exit-Code und nur die letzte Modellantwort

@pytest.mark.parametrize("mode", ["prompt_echo_exit1", "exit1_with_pong", "exit0_other_answer", "exit0_no_last_file"])
def test_codex_smoke_failure_restores_config_and_alarms(sb, mode):
    original = sb.config.read_text()
    r = sb.run(HOST_ONLY="1", FAKE_SMOKE_MODE=mode if mode != "prompt_echo_exit1" else "ok",
               FAKE_REJECT_MODEL="gpt-6.2-sol" if mode == "prompt_echo_exit1" else "")
    assert r.returncode == 1, sb.logtext
    assert sb.config.read_text() == original
    assert sb.config_model() == "gpt-6.1-sol"
    assert "Wechsel gpt-6.1-sol → gpt-6.2-sol fehlgeschlagen" in sb.alarms
    assert "wiederhergestellt" in sb.alarms
    assert "Host-Smoke codex fehlgeschlagen" in sb.logtext
    assert "Host-Smoke codex: ok" not in sb.logtext
    assert not sb.leftovers()
    assert len(list(sb.config.parent.glob("config.toml.bak-selfupdate-*"))) == 1


def test_codex_smoke_ignores_prompt_in_full_output(sb):
    """Der Gesamt-Output enthält den Prompt mit „pong“; ohne letzte Nachricht darf das nie als Erfolg zählen."""
    r = sb.run(HOST_ONLY="1", FAKE_SMOKE_MODE="exit0_no_last_file")
    assert r.returncode == 1
    assert sb.config_model() == "gpt-6.1-sol"


def test_codex_smoke_tolerant_comparison_accepts_case_and_punctuation(sb):
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0, sb.logtext
    assert "Host-Smoke codex: ok" in sb.logtext
    assert sb.config_model() == "gpt-6.2-sol"


@pytest.mark.parametrize("mode", ["prompt_echo_exit1", "exit1_with_pong", "exit0_other_answer", "other"])
def test_claude_smoke_checks_exit_code_and_answer(sb, mode):
    sb.set_host(claude="2.1.300")
    r = sb.run(HOST_ONLY="1", FAKE_SMOKE_MODE=mode)
    assert r.returncode == 1, sb.logtext
    assert sb.host_version("claude") == "2.1.300"
    assert "Host-CLI claude: Update 2.1.300 → 2.1.322 fehlgeschlagen" in sb.alarms
    assert "zurückgesetzt" in sb.alarms


def test_claude_smoke_ok_updates(sb):
    sb.set_host(claude="2.1.300")
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0, sb.logtext
    assert sb.host_version("claude") == "2.1.322"
    assert "Host-CLI claude aktualisiert: 2.1.300 → 2.1.322" in sb.alarms


@pytest.mark.parametrize("text", ["pong", "Pong.", " PONG!\n", "„pong“", "pong\n\n"])
def test_normalize_answer_accepts(text):
    r = subprocess.run(["bash", "-c", f'source "{SCRIPT}"; normalize_answer "$1"', "x", text], capture_output=True, text=True)
    assert r.stdout.strip() == "pong"


@pytest.mark.parametrize("text", ["pong pong", "Ich sage pong", "", "ping"])
def test_normalize_answer_rejects(text):
    r = subprocess.run(["bash", "-c", f'source "{SCRIPT}"; normalize_answer "$1"', "x", text], capture_output=True, text=True)
    assert r.stdout.strip() != "pong"


@pytest.mark.parametrize("content", ['{"models": [{"slug": "gpt-6.2-sol"', '[]', '{"other": 1}'])
def test_unreadable_models_cache_is_reported_and_changes_nothing(sb, content):
    sb.models.write_text(content)
    before = sb.config.read_bytes()
    r = sb.run(HOST_ONLY="1")
    assert r.returncode != 0, sb.logtext
    assert sb.config.read_bytes() == before
    assert "models_cache.json nicht auswertbar" in sb.alarms


def test_valid_models_cache_without_sol_is_skipped_quietly(sb):
    sb.models.write_text('{"models": [{"slug": "gpt-6.2-astra", "visibility": "list"}]}')
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0, sb.logtext
    assert "keine Sol-Version" in sb.logtext


# 2. config.toml: nur der Top-Level-Schlüssel

def _assert_only_model_changed(sb, before: dict, new="gpt-6.2-sol"):
    after = tomllib.loads(sb.config.read_text())
    assert after["model"] == new
    expected = dict(before)
    expected["model"] = new
    assert after == expected


def test_config_without_model_inserts_before_first_table(sb):
    sb.config.write_text('model_reasoning_effort = "high"\n\n[profiles.x]\nmodel = "o3"\n\n[projects."/a"]\ntrust_level = "trusted"\n')
    before = tomllib.loads(sb.config.read_text())
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0, sb.logtext
    _assert_only_model_changed(sb, before)
    text = sb.config.read_text()
    assert text.index('model = "gpt-6.2-sol"') < text.index("[profiles.x]")
    assert tomllib.loads(text)["profiles"]["x"]["model"] == "o3"
    assert "<nicht gesetzt> → gpt-6.2-sol" in sb.alarms


def test_config_with_only_profile_model_sets_top_level(sb):
    sb.config.write_text('[profiles.x]\nmodel = "gpt-6.1-sol"\n')
    before = tomllib.loads(sb.config.read_text())
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0, sb.logtext
    _assert_only_model_changed(sb, before)
    text = sb.config.read_text()
    assert text.index('model = "gpt-6.2-sol"') < text.index("[profiles.x]")


def test_config_empty_file_gets_model(sb):
    sb.config.write_text("")
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0, sb.logtext
    assert sb.config_model() == "gpt-6.2-sol"


def test_config_existing_model_replaced_profile_untouched(sb):
    sb.config.write_text('model = "gpt-6.1-sol"\n\n[profiles.x]\nmodel = "gpt-6.1-sol"\n')
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0, sb.logtext
    cfg = tomllib.loads(sb.config.read_text())
    assert cfg["model"] == "gpt-6.2-sol"
    assert cfg["profiles"]["x"]["model"] == "gpt-6.1-sol"


def test_config_header_like_line_in_multiline_string_is_not_a_table(sb):
    sb.config.write_text('notes = """\n[not a table]\n"""\n\n[projects."/a"]\ntrust_level = "trusted"\n')
    before = tomllib.loads(sb.config.read_text())
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0, sb.logtext
    _assert_only_model_changed(sb, before)
    text = sb.config.read_text()
    assert text.index("[not a table]") < text.index('model = "gpt-6.2-sol"') < text.index("[projects")


def test_config_keeps_permissions_and_crlf_and_other_content(sb):
    sb.config.write_bytes(b'# Kommentar\r\nmodel = "gpt-6.1-sol" # alt\r\nmodel_reasoning_effort = "medium"\r\n\r\n[projects."/a"]\r\ntrust_level = "trusted"\r\n')
    sb.config.chmod(0o640)
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0, sb.logtext
    raw = sb.config.read_bytes()
    assert raw.startswith(b'# Kommentar\r\nmodel = "gpt-6.2-sol"\r\nmodel_reasoning_effort = "medium"\r\n\r\n[projects."/a"]\r\ntrust_level = "trusted"\r\n')
    assert stat.S_IMODE(sb.config.stat().st_mode) == 0o640
    assert not sb.leftovers()


def test_config_same_model_is_not_touched(sb):
    sb.config.write_text('model = "gpt-6.2-sol"\n')
    mtime = sb.config.stat().st_mtime_ns
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0
    assert sb.config.stat().st_mtime_ns == mtime
    assert not list(sb.config.parent.glob("*.bak-selfupdate-*"))
    assert "Host-Modell: aktuell" in sb.logtext
    assert sb.alarms == ""


def test_config_invalid_toml_is_reported_and_not_touched(sb):
    sb.config.write_text("model = [unclosed\n")
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 1
    assert sb.config.read_text() == "model = [unclosed\n"
    assert "nicht auswertbar" in sb.alarms


def test_hidden_and_non_sol_models_are_ignored(sb):
    sb.models.write_text(json.dumps({"models": [{"slug": "gpt-7-sol", "visibility": "hide"}, {"slug": "gpt-6-luna", "visibility": "list"}, {"slug": "gpt-6.1-sol", "visibility": "list"}]}))
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0
    assert sb.config_model() == "gpt-6.1-sol"


def test_backup_failure_is_reported_and_config_untouched(sb):
    original = sb.config.read_text()
    r = sb.run(HOST_ONLY="1", FAKE_CP_FAIL="1")
    assert r.returncode == 1
    assert sb.config.read_text() == original
    assert "Sicherung der config.toml fehlgeschlagen" in sb.alarms
    assert "umgestellt" not in sb.alarms


def test_write_failure_is_reported_and_config_untouched(sb):
    original = sb.config.read_text()
    r = sb.run(HOST_ONLY="1", FAKE_MV_FAIL="1")
    assert r.returncode == 1
    assert sb.config.read_text() == original
    assert "Schreiben der config.toml" in sb.alarms and "fehlgeschlagen" in sb.alarms
    assert "umgestellt" not in sb.alarms
    assert not sb.leftovers()


def test_config_restore_failure_is_separate_alarm_and_nonzero(sb):
    r = sb.run(HOST_ONLY="1", FAKE_REJECT_MODEL="gpt-6.2-sol", FAKE_SABOTAGE_BACKUP="1")
    assert r.returncode == 2
    assert "WIEDERHERSTELLUNG der config.toml FEHLGESCHLAGEN" in sb.alarms


def test_backups_are_rotated_to_three(sb):
    for i in range(5):
        (sb.config.parent / f"config.toml.bak-selfupdate-2026010{i}-000000").write_text("x")
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0
    assert len(list(sb.config.parent.glob("config.toml.bak-selfupdate-*"))) == 3


# 3. Host-Abgleich läuft in jedem Ausstiegszweig

def _host_outdated(sb):
    sb.set_host(codex="0.170.0")


def _assert_host_synced(sb):
    assert sb.host_version("codex") == "0.171.0", sb.logtext
    assert sb.config_model() == "gpt-6.2-sol"
    assert "Host-CLI codex aktualisiert: 0.170.0 → 0.171.0" in sb.alarms


def test_branch_all_current_runs_sync_host(sb):
    _host_outdated(sb)
    r = sb.run()
    assert r.returncode == 0, sb.logtext
    assert "Alles aktuell" in sb.logtext
    _assert_host_synced(sb)


def test_branch_npm_view_empty_runs_sync_host(sb):
    _host_outdated(sb)
    r = sb.run(FAKE_NPM_VIEW_EMPTY="1")
    assert r.returncode == 0, sb.logtext
    assert "nicht ermittelbar" in sb.logtext
    _assert_host_synced(sb)


def test_branch_container_version_unreadable_runs_sync_host_without_crash(sb):
    (sb.fake / "ctr_claude").unlink()
    _host_outdated(sb)
    r = sb.run()
    assert r.returncode == 0, sb.logtext
    assert "Version von claude nicht ermittelbar" in sb.logtext
    _assert_host_synced(sb)


def test_branch_blocked_versions_runs_sync_host(sb):
    sb.set_container(claude="2.1.300")
    _host_outdated(sb)
    (sb.state).mkdir(exist_ok=True)
    (sb.state / "blocked-versions").write_text("claude=2.1.322 codex=0.171.0 gemini=0.5.0")
    r = sb.run()
    assert r.returncode == 0, sb.logtext
    assert "gesperrt, überspringe" in sb.logtext
    assert "compose build" not in sb.docker_log
    _assert_host_synced(sb)


def test_branch_gateway_busy_runs_sync_host(sb):
    sb.set_container(claude="2.1.300")
    _host_outdated(sb)
    r = sb.run(FAKE_BUSY="3")
    assert r.returncode == 0, sb.logtext
    assert "noch belegt" in sb.logtext
    assert "compose build" not in sb.docker_log
    _assert_host_synced(sb)


def test_branch_dry_run_syncs_dry_and_changes_nothing(sb):
    sb.set_container(claude="2.1.300")
    _host_outdated(sb)
    original = sb.config.read_text()
    r = sb.run(DRY_RUN="1")
    assert r.returncode == 0, sb.logtext
    assert "Update nötig" in sb.logtext
    assert "Host-Abgleich codex: 0.170.0 → 0.171.0" in sb.logtext
    assert "Host-Modell: gpt-6.1-sol → gpt-6.2-sol" in sb.logtext
    assert sb.logtext.count("DRY_RUN, keine Änderung.") == 3
    assert sb.host_version("codex") == "0.170.0"
    assert sb.config.read_text() == original
    assert not (sb.fake / "npm.log").exists() or "install" not in sb.read("npm.log")
    assert "compose" not in sb.docker_log and " tag " not in sb.docker_log
    assert sb.alarms == ""
    assert not list(sb.config.parent.glob("*.bak-selfupdate-*"))


def test_branch_build_failed_runs_sync_host_and_exits_nonzero(sb):
    sb.set_container(claude="2.1.300")
    _host_outdated(sb)
    r = sb.run(FAKE_BUILD_FAIL="1")
    assert r.returncode == 1, sb.logtext
    assert "Build fehlgeschlagen" in sb.alarms
    assert (sb.state / "blocked-versions").exists()
    _assert_host_synced(sb)


def test_branch_container_rollback_runs_sync_host_and_exits_nonzero(sb):
    sb.set_container(claude="2.1.300")
    _host_outdated(sb)
    r = sb.run(FAKE_SMOKE_FAIL="claude")
    assert r.returncode == 1, sb.logtext
    assert "ROLLBACK: Testanfrage über claude schlug fehl" in sb.logtext
    assert "Update fehlgeschlagen" in sb.alarms and "Altes Image zurückgespielt" in sb.alarms
    assert "ROLLBACK GESCHEITERT" not in sb.alarms
    _assert_host_synced(sb)


def test_branch_image_backup_failed_runs_sync_host_and_exits_nonzero(sb):
    sb.set_container(claude="2.1.300")
    _host_outdated(sb)
    r = sb.run(FAKE_TAG_FAIL="1")
    assert r.returncode == 1, sb.logtext
    assert "Image-Sicherung fehlgeschlagen" in sb.logtext
    _assert_host_synced(sb)


def test_worst_status_wins_when_container_ok_but_host_fails(sb):
    sb.set_container(claude="2.1.300")
    sb.set_host(codex="0.170.0")
    r = sb.run(FAKE_BROKEN_VERSION="0.171.0")
    assert r.returncode == 1, sb.logtext


def test_lock_is_respected_and_does_not_sync(sb):
    sb.state.mkdir(exist_ok=True)
    _host_outdated(sb)
    holder = subprocess.Popen(["bash", "-c", f'exec 9> "{sb.state}/lock"; flock -n 9 && echo locked && sleep 20'], stdout=subprocess.PIPE, text=True)
    try:
        assert holder.stdout.readline().strip() == "locked"
        r = sb.run()
        assert r.returncode == 0
        assert "Anderer Lauf aktiv" in sb.logtext
        assert sb.host_version("codex") == "0.170.0"
    finally:
        holder.kill()
        holder.wait()


# 4. Rollback der Host-CLI

def test_failed_host_update_is_rolled_back_with_ok_status_one(sb):
    _host_outdated(sb)
    r = sb.run(HOST_ONLY="1", FAKE_BROKEN_VERSION="0.171.0")
    assert r.returncode == 1, sb.logtext
    assert sb.host_version("codex") == "0.170.0"
    assert "Update 0.170.0 → 0.171.0 fehlgeschlagen" in sb.alarms
    assert "auf 0.170.0 zurückgesetzt" in sb.alarms
    assert "ROLLBACK" not in sb.alarms


def test_failed_rollback_is_separate_alarm_and_exit_nonzero(sb):
    _host_outdated(sb)
    r = sb.run(HOST_ONLY="1", FAKE_BROKEN_VERSION="0.171.0", FAKE_NPM_FAIL_VERSIONS="0.170.0")
    assert r.returncode == 2, sb.logtext
    assert "ROLLBACK auf 0.170.0 FEHLGESCHLAGEN" in sb.alarms
    assert "zurückgesetzt" not in sb.alarms


def test_rollback_that_leaves_wrong_version_counts_as_failed(sb):
    _host_outdated(sb)
    r = sb.run(HOST_ONLY="1", FAKE_BROKEN_VERSION="0.171.0", FAKE_NPM_NOOP_VERSIONS="0.170.0")
    assert r.returncode == 2, sb.logtext
    assert "ROLLBACK auf 0.170.0 FEHLGESCHLAGEN (installiert: '0.171.0')" in sb.alarms
    assert "zurückgesetzt" not in sb.alarms


def test_install_exit_zero_but_version_unchanged_is_failure_with_rollback(sb):
    _host_outdated(sb)
    r = sb.run(HOST_ONLY="1", FAKE_NPM_NOOP_VERSIONS="0.171.0")
    assert r.returncode == 1, sb.logtext
    assert "nach npm install ist '0.170.0' installiert statt 0.171.0" in sb.alarms
    assert "zurückgesetzt" in sb.alarms


def test_npm_install_failure_triggers_rollback(sb):
    _host_outdated(sb)
    r = sb.run(HOST_ONLY="1", FAKE_NPM_FAIL_VERSIONS="0.171.0")
    assert r.returncode == 1, sb.logtext
    assert "npm install fehlgeschlagen" in sb.alarms
    assert sb.host_version("codex") == "0.170.0"


def test_host_cli_never_downgrades(sb):
    sb.set_host(codex="0.180.0")
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0
    assert sb.host_version("codex") == "0.180.0"
    assert "install" not in sb.read("npm.log")


def test_npm_install_uses_host_prefix(sb):
    _host_outdated(sb)
    sb.run(HOST_ONLY="1")
    assert f"--prefix {sb.prefix}" in sb.read("npm.log")


# 4b. Zielkombination: neue CLI + neues Modell, wenn die neue CLI das alte Modell ablehnt

OLD_CACHE = json.dumps({"models": [{"slug": "gpt-6.1-sol", "visibility": "list"}]})


def _combo_env(accept: str) -> dict[str, str]:
    """Cache kennt zunächst nur die alte Sol-Version; erst der Lauf der neuen CLI (0.171.0) frischt ihn auf."""
    return {
        "FAKE_CODEX_ACCEPT": accept,
        "FAKE_CODEX_CACHE_REFRESH": json.dumps(MODELS_CACHE),
        "FAKE_CODEX_CACHE_REFRESH_VERSION": "0.171.0",
    }


def test_new_cli_only_accepts_new_model_installs_both(sb):
    _host_outdated(sb)
    sb.models.write_text(OLD_CACHE)
    r = sb.run(HOST_ONLY="1", **_combo_env("0.171.0:gpt-6.2-sol 0.170.0:gpt-6.1-sol"))
    assert r.returncode == 0, sb.logtext
    assert sb.host_version("codex") == "0.171.0"
    assert sb.config_model() == "gpt-6.2-sol"
    calls = sb.read("codex_calls.log").splitlines()
    assert "0.171.0 model=gpt-6.1-sol cfg=gpt-6.1-sol" in calls
    assert "0.171.0 model=gpt-6.2-sol cfg=gpt-6.1-sol" in calls
    assert "@openai/codex@0.170.0" not in sb.read("npm.log")
    assert "zweiter Smoke der neuen CLI mit Zielmodell gpt-6.2-sol" in sb.logtext
    assert "Host-Smoke codex: ok (Modell gpt-6.2-sol)" in sb.logtext
    assert "neue CLI 0.171.0 bleibt installiert" in sb.logtext
    assert "Rollback" not in sb.logtext
    assert "Host-CLI codex aktualisiert: 0.170.0 → 0.171.0" in sb.alarms
    assert "mit dem Zielmodell gpt-6.2-sol bestand die neue CLI" in sb.alarms
    assert "Host-Standardmodell codex umgestellt: gpt-6.1-sol → gpt-6.2-sol" in sb.alarms
    assert "zurückgesetzt" not in sb.alarms and "fehlgeschlagen" not in sb.alarms
    assert not sb.leftovers()


def test_new_cli_accepts_nothing_rolls_back_as_before(sb):
    _host_outdated(sb)
    sb.models.write_text(OLD_CACHE)
    original = sb.config.read_text()
    r = sb.run(HOST_ONLY="1", **_combo_env("0.170.0:gpt-6.1-sol"))
    assert r.returncode == 1, sb.logtext
    assert sb.host_version("codex") == "0.170.0"
    assert sb.config.read_text() == original
    calls = sb.read("codex_calls.log").splitlines()
    assert "0.171.0 model=gpt-6.2-sol cfg=gpt-6.1-sol" in calls
    assert "Host-Smoke codex fehlgeschlagen (Modell gpt-6.2-sol)" in sb.logtext
    assert "Update 0.170.0 → 0.171.0 fehlgeschlagen (Testanfrage fehlgeschlagen)" in sb.alarms
    assert "auf 0.170.0 zurückgesetzt" in sb.alarms
    assert "bestand die neue CLI" not in sb.alarms
    assert "Host-CLI codex aktualisiert" not in sb.alarms


COMBO = "0.171.0:gpt-6.2-sol 0.170.0:gpt-6.1-sol"


def _assert_combo_rolled_back_as_pair(sb):
    assert sb.host_version("codex") == "0.170.0", sb.logtext
    assert sb.config_model() == "gpt-6.1-sol"
    assert "Host-CLI codex aktualisiert" not in sb.alarms
    assert "neue CLI läuft nur mit Zielmodell gpt-6.2-sol" in sb.alarms
    assert "Update 0.170.0 → 0.171.0 fehlgeschlagen" in sb.alarms
    assert "auf 0.170.0 zurückgesetzt" in sb.alarms


def test_combo_cli_rolled_back_when_model_backup_fails(sb):
    _host_outdated(sb)
    sb.models.write_text(OLD_CACHE)
    original = sb.config.read_text()
    r = sb.run(HOST_ONLY="1", FAKE_CP_FAIL="1", **_combo_env(COMBO))
    assert r.returncode == 1, sb.logtext
    assert sb.config.read_text() == original
    assert "Sicherung der config.toml fehlgeschlagen" in sb.alarms
    _assert_combo_rolled_back_as_pair(sb)


def test_combo_cli_rolled_back_when_model_write_fails(sb):
    _host_outdated(sb)
    sb.models.write_text(OLD_CACHE)
    r = sb.run(HOST_ONLY="1", FAKE_MV_FAIL="1", **_combo_env(COMBO))
    assert r.returncode == 1, sb.logtext
    assert "Schreiben der config.toml" in sb.alarms
    _assert_combo_rolled_back_as_pair(sb)


def test_combo_cli_rolled_back_when_final_smoke_fails(sb):
    _host_outdated(sb)
    sb.models.write_text(OLD_CACHE)
    original = sb.config.read_text()
    r = sb.run(HOST_ONLY="1", FAKE_CODEX_REJECT_IMPLICIT="gpt-6.2-sol", **_combo_env(COMBO))
    assert r.returncode == 1, sb.logtext
    assert sb.config.read_text() == original
    assert "Wechsel gpt-6.1-sol → gpt-6.2-sol fehlgeschlagen, gpt-6.1-sol wiederhergestellt" in sb.alarms
    _assert_combo_rolled_back_as_pair(sb)


def test_combo_cli_rollback_failure_is_status_two(sb):
    _host_outdated(sb)
    sb.models.write_text(OLD_CACHE)
    r = sb.run(HOST_ONLY="1", FAKE_CP_FAIL="1", FAKE_NPM_FAIL_VERSIONS="0.170.0", **_combo_env(COMBO))
    assert r.returncode == 2, sb.logtext
    assert "ROLLBACK auf 0.170.0 FEHLGESCHLAGEN" in sb.alarms
    assert "Host-CLI codex aktualisiert" not in sb.alarms


def test_combo_cli_kept_when_model_restore_itself_fails(sb):
    """Status 2 des Modellwechsels: der Stand der config.toml ist ungewiss, die CLI wird nicht angefasst."""
    _host_outdated(sb)
    sb.models.write_text(OLD_CACHE)
    r = sb.run(HOST_ONLY="1", FAKE_CODEX_REJECT_IMPLICIT="gpt-6.2-sol", FAKE_SABOTAGE_BACKUP="1", **_combo_env(COMBO))
    assert r.returncode == 2, sb.logtext
    assert sb.host_version("codex") == "0.171.0"
    assert "CLI nicht zurückgesetzt" in sb.alarms
    assert "@openai/codex@0.170.0" not in sb.read("npm.log")


def test_no_second_smoke_when_target_model_equals_configured(sb):
    _host_outdated(sb)
    sb.config.write_text('model = "gpt-6.2-sol"\n')
    r = sb.run(HOST_ONLY="1", FAKE_CODEX_ACCEPT="0.170.0:gpt-6.2-sol")
    assert r.returncode == 1, sb.logtext
    assert sb.host_version("codex") == "0.170.0"
    assert "kein zweiter Smoke" in sb.logtext
    assert sb.read("codex_calls.log").splitlines().count("0.171.0 model=gpt-6.2-sol cfg=gpt-6.2-sol") == 1


def test_second_smoke_is_not_used_for_npm_failures(sb):
    _host_outdated(sb)
    r = sb.run(HOST_ONLY="1", FAKE_NPM_FAIL_VERSIONS="0.171.0")
    assert r.returncode == 1, sb.logtext
    assert "zweiter Smoke" not in sb.logtext


# 5. HOST_ONLY

def test_host_only_failure_exits_nonzero(sb):
    r = sb.run(HOST_ONLY="1", FAKE_REJECT_MODEL="gpt-6.2-sol")
    assert r.returncode != 0
    assert "Nur Host-Abgleich" in sb.logtext
    assert "compose" not in sb.docker_log


def test_host_only_success_exits_zero_and_skips_container_part(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(HOST_ONLY="1")
    assert r.returncode == 0, sb.logtext
    assert "compose" not in sb.docker_log
    assert "Update nötig" not in sb.logtext


# 6. Erfolgsfall (voller Pfad) und Schlüssel nie in argv

def test_full_success_switches_models_and_reports(sb):
    sb.set_container(claude="2.1.300", codex="0.170.0")
    sb.set_host(claude="2.1.300", codex="0.170.0")
    (sb.fake / "models_claude.after.json").write_text(json.dumps({"data": [{"id": "claude-haiku-latest"}], "x_aliases": {"fable": "claude-fable-5-2"}}))
    (sb.state).mkdir(exist_ok=True)
    (sb.state / "blocked-versions").write_text("alt")

    r = sb.run(FAKE_OPENWEBUI="1")
    assert r.returncode == 0, sb.logtext
    assert "compose build --no-cache cli-proxy" in sb.docker_log
    assert "Gateway aktualisiert" in sb.alarms
    assert "Modellliste geändert" in sb.logtext
    assert "fable=claude-fable-5-2" in sb.alarms
    assert "restart openwebui" in sb.docker_log
    assert not (sb.state / "blocked-versions").exists()
    assert "rmi geef-atelier-cli-proxy:prev-20260101-000000-selfupdate" in sb.docker_log
    assert sb.config_model() == "gpt-6.2-sol"
    assert "Host-Standardmodell codex umgestellt: gpt-6.1-sol → gpt-6.2-sol" in sb.alarms
    assert "Fertig (Status 0)" in sb.logtext
    assert sb.leftovers() == []


def test_gateway_key_never_in_argv_or_log(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run()
    assert r.returncode == 0, sb.logtext
    argv = sb.read("curl.argv")
    assert "chat/completions" in argv
    assert KEY not in argv
    assert KEY not in sb.docker_log and KEY not in sb.alarms and KEY not in sb.logtext
    assert KEY not in r.stdout and KEY not in r.stderr
    assert "-H @" in argv
    auth = sb.read("curl.auth").split()
    assert auth and set(auth) == {"auth-file-ok"}


def test_gateway_key_header_file_is_removed(sb):
    sb.set_container(claude="2.1.300")
    (sb.root / "tmp").mkdir()
    r = sb.run(TMPDIR=str(sb.root / "tmp"))
    assert r.returncode == 0, sb.logtext
    assert list((sb.root / "tmp").iterdir()) == []


def test_compose_up_failure_rolls_back(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_UP_FAIL="1")
    assert r.returncode == 1, sb.logtext
    assert "ROLLBACK: docker compose up fehlgeschlagen" in sb.logtext
    assert (sb.state / "blocked-versions").exists()


def test_smoke_without_key_sends_no_auth_header(sb):
    (sb.project / ".env").write_text("OTHER=1\n")
    sb.set_container(claude="2.1.300")
    r = sb.run()
    assert r.returncode == 0, sb.logtext
    assert sb.read("curl.auth") == ""


# 6b. Neubau nur aus dem committeten Stand von cli-proxy/

def test_clean_tree_builds_and_checks_only_cli_proxy(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run()
    assert r.returncode == 0, sb.logtext
    assert f"-C {sb.project} status --porcelain -- cli-proxy" in sb.read("git.log")
    assert "compose build --no-cache cli-proxy" in sb.docker_log
    assert "ausgesetzt" not in sb.logtext


@pytest.mark.parametrize("extra", [{}, {"FORCE": "1"}])
def test_dirty_tree_suspends_build_but_syncs_host(sb, extra):
    sb.set_container(claude="2.1.300")
    _host_outdated(sb)
    r = sb.run(FAKE_GIT_DIRTY="M_cli-proxy/app.py ??_cli-proxy/neu.py", **extra)
    assert r.returncode == 0, sb.logtext
    assert "Image-Neubau ausgesetzt (vor der Ruhewartezeit): nicht committete Änderungen unter cli-proxy/ (2 Einträge" in sb.logtext
    assert "Image-Neubau ausgesetzt" in sb.alarms and "nicht committete Änderungen" in sb.alarms
    assert "compose" not in sb.docker_log and " tag " not in sb.docker_log
    assert not (sb.state / "blocked-versions").exists()
    assert "Fertig (Status 0, ausgesetzt)." in sb.logtext
    _assert_host_synced(sb)


def test_unreadable_git_state_suspends_build(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_GIT_FAIL="1")
    assert r.returncode == 0, sb.logtext
    assert "nicht prüfbar" in sb.logtext and "nicht prüfbar" in sb.alarms
    assert "compose" not in sb.docker_log
    assert "Fertig (Status 0, ausgesetzt)." in sb.logtext


def test_dirty_tree_is_not_checked_when_nothing_to_update(sb):
    r = sb.run(FAKE_GIT_DIRTY="M_cli-proxy/app.py")
    assert r.returncode == 0, sb.logtext
    assert "Alles aktuell" in sb.logtext
    assert sb.read("git.log") == ""
    assert sb.alarms == "" or "ausgesetzt" not in sb.alarms


def _status_checks(sb) -> int:
    return sum(1 for line in sb.read("git.log").splitlines() if " status --porcelain -- cli-proxy" in line)


def _assert_suspended_before_build(sb, r):
    assert r.returncode == 0, sb.logtext
    assert "compose build" not in sb.docker_log and "compose up" not in sb.docker_log
    assert not (sb.state / "blocked-versions").exists()
    assert "Fertig (Status 0, ausgesetzt)." in sb.logtext
    assert "Image-Neubau ausgesetzt" in sb.alarms


def test_tree_is_checked_before_wait_before_build_and_before_swap(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run()
    assert r.returncode == 0, sb.logtext
    assert _status_checks(sb) == 3
    log = sb.read("git.log")
    assert "ls-files --others --ignored --exclude-standard -- cli-proxy/pyproject.toml cli-proxy/src" in log
    assert "rev-parse --verify --quiet HEAD:cli-proxy" in log
    assert "compose up -d --no-deps cli-proxy" in sb.docker_log


def test_edit_during_quiet_wait_is_caught_right_before_build(sb):
    sb.set_container(claude="2.1.300")
    _host_outdated(sb)
    r = sb.run(FAKE_BUSY_CALLS="2", FAKE_DIRTY_DURING_WAIT="1", WAIT_QUIET_SECONDS="300")
    _assert_suspended_before_build(sb, r)
    assert "Gateway nach 60s ruhig." in sb.logtext
    assert "Image-Neubau ausgesetzt (unmittelbar vor dem Bauen): nicht committete Änderungen" in sb.logtext
    assert _status_checks(sb) == 2
    _assert_host_synced(sb)


def test_dirty_on_second_check_suspends_even_with_force(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_GIT_DIRTY_FROM_CALL="2", FORCE="1")
    _assert_suspended_before_build(sb, r)
    assert "unmittelbar vor dem Bauen" in sb.logtext


def _assert_swap_refused(sb, r, reason):
    assert r.returncode == 0, sb.logtext
    assert "compose build --no-cache cli-proxy" in sb.docker_log
    assert "compose up" not in sb.docker_log
    assert reason in sb.logtext
    assert "Kein Austausch des Containers" in sb.logtext
    assert "Image-Neubau verworfen" in sb.alarms and "unverändert" in sb.alarms
    assert _tag_value(sb, "latest") == "sha256:running-before"
    assert not (sb.fake / "ctr_image").exists()
    assert not (sb.state / "blocked-versions").exists()
    assert "Fertig (Status 0, ausgesetzt)." in sb.logtext


def test_edit_during_build_prevents_swap_and_restores_latest(sb):
    sb.set_container(claude="2.1.300")
    _host_outdated(sb)
    r = sb.run(FAKE_DIRTY_DURING_BUILD="1", FAKE_RUNNING_BEFORE="running-before", FAKE_TAG_BEFORE="tag-before")
    _assert_swap_refused(sb, r, "nicht committete Änderungen unter cli-proxy/")
    _assert_host_synced(sb)


def test_commit_during_build_prevents_swap(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_TREE_DURING_BUILD="2222222222222222222222222222222222222222", FAKE_RUNNING_BEFORE="running-before")
    _assert_swap_refused(sb, r, "während des Builds gewechselt (111111111111 → 222222222222)")


def test_refused_swap_with_failed_retag_is_status_two(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_DIRTY_DURING_BUILD="1", FAKE_ROLLBACK_TAG_FAIL="1")
    assert r.returncode == 2, sb.logtext
    assert "compose up" not in sb.docker_log
    assert "FEHLGESCHLAGEN" in sb.alarms
    assert "Fertig (Status 2)." in sb.logtext


@pytest.mark.parametrize(
    "ignored, counted",
    [
        ("cli-proxy/src/lokaler_patch.py", 1),
        ("cli-proxy/pyproject.toml.orig cli-proxy/src/adapters/geheim.json", 2),
        ("cli-proxy/src/modul.pyc", 1),
        ("cli-proxy/src/__pycache__/main.cpython-312.pyc cli-proxy/src/neu.py", 1),
    ],
)
def test_ignored_files_copied_into_image_suspend_build(sb, ignored, counted):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_GIT_IGNORED=ignored)
    _assert_suspended_before_build(sb, r)
    assert f"ignorierte, aber ins Image kopierte Dateien unter cli-proxy/ ({counted} Einträge" in sb.logtext
    assert "__pycache__" not in sb.logtext.split("ignorierte, aber ins Image kopierte Dateien")[1].splitlines()[0]


def test_harmless_ignored_files_do_not_block_build(sb):
    sb.set_container(claude="2.1.300")
    harmless = ("cli-proxy/src/__pycache__/main.cpython-312.pyc cli-proxy/src/adapters/__pycache__/x.pyc "
                "cli-proxy/src/.pytest_cache/v/cache/nodeids cli-proxy/src/atelier_cli_proxy.egg-info/PKG-INFO")
    r = sb.run(FAKE_GIT_IGNORED=harmless)
    assert r.returncode == 0, sb.logtext
    assert "ausgesetzt" not in sb.logtext
    assert "compose up -d --no-deps cli-proxy" in sb.docker_log


@pytest.mark.parametrize("env", [{"FAKE_GIT_LSFILES_FAIL": "1"}, {"FAKE_GIT_NO_TREE": "1"}])
def test_unverifiable_ignored_files_or_tree_suspends_build(sb, env):
    sb.set_container(claude="2.1.300")
    r = sb.run(**env)
    _assert_suspended_before_build(sb, r)


def test_image_source_paths_match_dockerfile_copy():
    dockerfile = SCRIPT.parents[1] / "cli-proxy" / "Dockerfile"
    copied = set()
    for line in dockerfile.read_text().splitlines():
        parts = line.split()
        if parts and parts[0].upper() in ("COPY", "ADD"):
            sources = [p for p in parts[1:-1] if not p.startswith("--")]
            copied.update("cli-proxy/" + s.rstrip("/").removeprefix("./") for s in sources)
    assert not (SCRIPT.parents[1] / "cli-proxy" / ".dockerignore").exists()
    r = subprocess.run(["bash", "-c", f'source "{SCRIPT}"; printf "%s\\n" "${{IMAGE_SOURCE_PATHS[@]}}"'],
                       capture_output=True, text=True, timeout=30)
    assert set(r.stdout.split()) == copied


# 7. sourcebar

def test_script_is_sourceable_without_side_effects(sb):
    r = subprocess.run(
        ["bash", "-c", f'source "{SCRIPT}"; semver_lt 0.9.0 0.10.0 && echo lt; echo "$STATE_DIR"'],
        env={**sb.base_env()}, capture_output=True, text=True, timeout=30,
    )
    assert r.returncode == 0, r.stderr
    assert r.stdout.split() == ["lt", str(sb.state)]
    assert not sb.state.exists()
    assert not sb.log.exists()


def test_paths_default_to_production_values_when_unset():
    r = subprocess.run(
        ["env", "-i", "PATH=/usr/bin:/bin", "bash", "-c",
         f'source "{SCRIPT}"; echo "$PROJECT_DIR|$CONTAINER|$STATE_DIR|$LOG|$GATEWAY|$HOST_NPM_PREFIX|$HOST_CODEX_CONFIG|$HOST_CODEX_MODELS|$ALARM_CMD"'],
        capture_output=True, text=True, timeout=30,
    )
    assert r.stdout.strip() == ("/srv/docker/websites/geef_atelier|geef-atelier-cli-proxy|/srv/backup/cli-proxy-update|"
                                "/srv/backup/cli-proxy-update/update.log|http://localhost:8090|/home/developer/.npm-global|"
                                "/home/developer/.codex/config.toml|/home/developer/.codex/models_cache.json|hermes send --to telegram")


def test_real_files_untouched_marker(sb):
    """Absicherung: die Tests dürfen nie echte Pfade benutzen."""
    env = sb.base_env()
    for k in ("STATE_DIR", "LOG", "HOST_NPM_PREFIX", "HOST_CODEX_CONFIG", "HOST_CODEX_MODELS", "PROJECT_DIR"):
        assert env[k].startswith(str(sb.root))
    assert shutil.which("hermes", path=env["SELFUPDATE_PATH"]) is None


# 8. Container-Smoke: strenger Prüfvertrag

def _smoke(sb, backend="claude", model="claude-haiku-latest", **extra):
    env = {**sb.base_env(), "PATH": f"{sb.bin}:/usr/bin:/bin", **extra}
    r = subprocess.run(
        ["bash", "-c", f'source "{SCRIPT}"; smoke "$1" "$2"; echo "smoke_rc=$?"', "x", backend, model],
        env=env, capture_output=True, text=True, timeout=60,
    )
    rc = int(r.stdout.strip().splitlines()[-1].split("=")[1])
    return rc, r.stdout


def test_smoke_success_and_normalized_answer(sb):
    rc, out = _smoke(sb, FAKE_SMOKE_CONTENT="Pong.")
    assert rc == 0, out
    assert "FEHLGESCHLAGEN" not in out


def test_smoke_success_codex(sb):
    rc, out = _smoke(sb, "codex", "gpt-6.2-sol")
    assert rc == 0, out


@pytest.mark.parametrize(
    ("extra", "reason"),
    [
        ({"FAKE_SMOKE_CURL_RC": "18"}, "curl Exit 18"),
        ({"FAKE_SMOKE_NO_PROVIDER": "1"}, "Header x-cli-proxy-provider fehlt"),
        ({"FAKE_SMOKE_PROVIDER": "codex"}, "Provider 'codex' statt 'claude'"),
        ({"FAKE_SMOKE_FAILOVER": "claude->codex"}, "Failover-Header gesetzt"),
        ({"FAKE_SMOKE_CONTENT": "Hallo"}, "Antwort nicht 'pong'"),
        ({"FAKE_SMOKE_CONTENT": "Ich sage pong"}, "Antwort nicht 'pong'"),
        ({"FAKE_SMOKE_CONTENT": ""}, "Antwort nicht 'pong'"),
        ({"FAKE_SMOKE_FAIL": "claude"}, "HTTP 500"),
    ],
)
def test_smoke_rejects(sb, extra, reason):
    rc, out = _smoke(sb, **extra)
    assert rc != 0, out
    assert reason in out


def test_smoke_failure_in_full_run_rolls_back_on_missing_provider_header(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_SMOKE_NO_PROVIDER="1")
    assert r.returncode == 1, sb.logtext
    assert "ROLLBACK: Testanfrage über claude schlug fehl" in sb.logtext


# 9. Ehrlicher Rollback-Status

def _rollback_run(sb, **extra):
    sb.set_container(claude="2.1.300")
    return sb.run(FAKE_SMOKE_FAIL="claude", **extra)


def test_rollback_success_is_status_one(sb):
    r = _rollback_run(sb)
    assert r.returncode == 1, sb.logtext
    assert "Altes Image zurückgespielt: ja, Gateway wieder gesund: yes" in sb.alarms
    assert "ROLLBACK GESCHEITERT" not in sb.alarms
    assert "Fertig (Status 1)" in sb.logtext
    assert (sb.state / "blocked-versions").exists()


def test_rollback_tag_failure_is_status_two(sb):
    r = _rollback_run(sb, FAKE_ROLLBACK_TAG_FAIL="1")
    assert r.returncode == 2, sb.logtext
    assert "ROLLBACK GESCHEITERT" in sb.alarms and "Zurück-Taggen: nein" in sb.alarms
    assert "Altes Image zurückgespielt" not in sb.alarms


def test_rollback_compose_up_failure_is_status_two(sb):
    r = _rollback_run(sb, FAKE_ROLLBACK_UP_FAIL="1")
    assert r.returncode == 2, sb.logtext
    assert "ROLLBACK GESCHEITERT" in sb.alarms and "compose up: nein" in sb.alarms


def test_rollback_unhealthy_gateway_is_status_two(sb):
    r = _rollback_run(sb, FAKE_UNHEALTHY_AFTER_ROLLBACK="1")
    assert r.returncode == 2, sb.logtext
    assert "ROLLBACK GESCHEITERT" in sb.alarms and "Gateway gesund: no" in sb.alarms


def test_rollback_image_id_mismatch_is_status_two(sb):
    r = _rollback_run(sb, FAKE_CTR_IMAGE_ID="bbb")
    assert r.returncode == 2, sb.logtext
    assert "ROLLBACK GESCHEITERT" in sb.alarms and "Container hat altes Image: nein" in sb.alarms
    assert "Container-Image 'sha256:bbb' != Sicherung 'sha256:aaa'" in sb.logtext


def test_compose_up_failure_with_failed_rollback_is_status_two(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_UP_FAIL="1", FAKE_ROLLBACK_TAG_FAIL="1")
    assert r.returncode == 2, sb.logtext


def test_build_failure_with_failed_retag_is_status_two(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_BUILD_FAIL="1", FAKE_ROLLBACK_TAG_FAIL="1")
    assert r.returncode == 2, sb.logtext
    assert "Zurück-Taggen" in sb.alarms and "FEHLGESCHLAGEN" in sb.alarms
    assert (sb.state / "blocked-versions").exists()


def test_build_failure_with_ok_retag_stays_status_one(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_BUILD_FAIL="1")
    assert r.returncode == 1, sb.logtext
    assert "FEHLGESCHLAGEN" not in sb.alarms


# 9b. Rollback-Ziel ist das laufende Image, nicht `latest`

IMG = "geef-atelier-cli-proxy"


def _tag_value(sb, name):
    f = sb.fake / ("img_" + (f"{IMG}:{name}").replace("/", "_").replace(":", "_"))
    return f.read_text().strip() if f.exists() else None


def test_rollback_restores_running_image_when_latest_diverges(sb):
    r = _rollback_run(sb, FAKE_RUNNING_BEFORE="running-before", FAKE_TAG_BEFORE="tag-before")
    assert r.returncode == 1, sb.logtext
    assert (sb.fake / "ctr_image").read_text().strip() == "sha256:running-before"
    assert _tag_value(sb, "latest") == "sha256:running-before"
    prev = [ln for ln in sb.docker_log.splitlines() if ln.startswith(f"tag sha256:running-before {IMG}:prev-")]
    assert len(prev) == 1, sb.docker_log
    assert f"tag {IMG}:latest {IMG}:prev-" not in sb.docker_log
    assert "tag-before" not in "".join(p.read_text() for p in sb.fake.glob("img_*prev-*"))
    assert "Altes Image zurückgespielt: ja" in sb.alarms
    assert "ROLLBACK GESCHEITERT" not in sb.alarms
    assert "Laufender Container fake-ctr nutzt Image sha256:running-before" in sb.logtext


def test_build_failure_restores_running_image_when_latest_diverges(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_BUILD_FAIL="1", FAKE_RUNNING_BEFORE="running-before", FAKE_TAG_BEFORE="tag-before")
    assert r.returncode == 1, sb.logtext
    assert _tag_value(sb, "latest") == "sha256:running-before"
    assert f"tag sha256:running-before {IMG}:latest" in sb.docker_log


def test_rollback_that_ends_on_latest_image_instead_of_running_is_status_two(sb):
    r = _rollback_run(sb, FAKE_RUNNING_BEFORE="running-before", FAKE_TAG_BEFORE="tag-before", FAKE_CTR_IMAGE_ID="tag-before")
    assert r.returncode == 2, sb.logtext
    assert "Container-Image 'sha256:tag-before' != Sicherung 'sha256:running-before'" in sb.logtext
    assert "Container hat altes Image: nein" in sb.alarms


def test_running_image_unknown_falls_back_to_latest(sb):
    r = _rollback_run(sb, FAKE_RUNNING_UNKNOWN="1", FAKE_TAG_BEFORE="tag-before")
    assert r.returncode == 1, sb.logtext
    assert "nicht ermittelbar, Rückfall: Sicherung von geef-atelier-cli-proxy:latest" in sb.logtext
    assert f"tag {IMG}:latest {IMG}:prev-" in sb.docker_log
    assert _tag_value(sb, "latest") == "sha256:tag-before"
    assert (sb.fake / "ctr_image").read_text().strip() == "sha256:tag-before"
    assert "ROLLBACK GESCHEITERT" not in sb.alarms


def test_running_image_unknown_with_build_failure_falls_back_to_latest(sb):
    sb.set_container(claude="2.1.300")
    r = sb.run(FAKE_BUILD_FAIL="1", FAKE_RUNNING_UNKNOWN="1", FAKE_TAG_BEFORE="tag-before")
    assert r.returncode == 1, sb.logtext
    assert _tag_value(sb, "latest") == "sha256:tag-before"


# 10. Mehrzeiliges TOML-model: bewusst nicht unterstützt, fail-safe

def test_multiline_top_level_model_fails_safe(sb):
    raw = b'model = """\ngpt-6.1-sol\n"""\nmodel_reasoning_effort = "medium"\n\n[projects."/a"]\ntrust_level = "trusted"\n'
    sb.config.write_bytes(raw)
    r = sb.run(HOST_ONLY="1")
    assert r.returncode != 0, sb.logtext
    assert sb.config.read_bytes() == raw
    assert "fehlgeschlagen" in sb.alarms and "unverändert" in sb.alarms
    assert "umgestellt" not in sb.alarms
    assert "toml_model:" in sb.logtext
    assert not sb.leftovers()
