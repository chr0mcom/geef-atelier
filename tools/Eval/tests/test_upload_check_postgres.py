"""The queries of ``upload_check`` against a real Postgres: same answers as the fake of the dry run, and no write gets through.

Runs only with ``EVAL_PG_TEST_CONTAINER`` = name of a running, disposable Postgres container (its tables are dropped and created
again); never against a database with real data. Example::

    docker run -d --rm --name upload-check-pg -e POSTGRES_USER=probe -e POSTGRES_PASSWORD=probe -e POSTGRES_DB=probe postgres:16-alpine
    EVAL_PG_TEST_CONTAINER=upload-check-pg python3 -m pytest -q tests/test_upload_check_postgres.py
    docker rm -f upload-check-pg
"""
import os
import subprocess
import time
from datetime import datetime

import pytest

from geef_eval import upload_check as uc
from upload_world import Scenario

CONTAINER = os.environ.get("EVAL_PG_TEST_CONTAINER")
pytestmark = pytest.mark.skipif(not CONTAINER, reason="EVAL_PG_TEST_CONTAINER nicht gesetzt")

SCHEMA = """
DROP TABLE IF EXISTS "SourceRefusals", "UploadTickets", "RunSourceUsages", "Runs", "OrderSources", "Orders", "Sources" CASCADE;
CREATE TABLE "Sources"("Id" uuid PRIMARY KEY, "OwnerUsername" text NOT NULL, "FileName" varchar(255) NOT NULL,
  "Kind" varchar(20) NOT NULL, "Sha256" character(64) NOT NULL, "CharCount" integer NOT NULL, "Origin" varchar(20) NOT NULL,
  "ExtractedText" text NULL, "CreatedAt" timestamptz NOT NULL);
CREATE TABLE "Orders"("Id" uuid PRIMARY KEY, "CreatedByUser" text NULL, "RequestText" text NOT NULL DEFAULT '');
CREATE TABLE "OrderSources"("OrderId" uuid NOT NULL REFERENCES "Orders"("Id"), "SourceId" uuid NOT NULL REFERENCES "Sources"("Id"),
  "Role" varchar(30) NOT NULL DEFAULT 'Facts', PRIMARY KEY ("OrderId", "SourceId"));
CREATE TABLE "Runs"("Id" uuid PRIMARY KEY, "OrderId" uuid NULL REFERENCES "Orders"("Id"), "Status" text NOT NULL);
CREATE TABLE "RunSourceUsages"("Id" uuid PRIMARY KEY, "RunId" uuid NOT NULL REFERENCES "Runs"("Id"), "SourceId" uuid NULL,
  "FileName" varchar(255) NOT NULL DEFAULT '', "Mode" varchar(20) NOT NULL, "Target" varchar(20) NOT NULL,
  "CharsIncluded" integer NOT NULL);
CREATE TABLE "UploadTickets"("Id" uuid PRIMARY KEY, "OwnerUsername" text NOT NULL, "FileName" varchar(255) NOT NULL,
  "DeclaredBytes" bigint NULL, "TokenHash" char(64) NOT NULL, "State" varchar(16) NOT NULL, "Attempts" int NOT NULL DEFAULT 0,
  "SourceId" uuid NULL REFERENCES "Sources"("Id") ON DELETE SET NULL, "CreatedAt" timestamptz NOT NULL,
  "TokenExpiresAt" timestamptz NOT NULL, "ExpiresAt" timestamptz NOT NULL, "ReceivingSince" timestamptz NULL,
  "UsedAt" timestamptz NULL, "LastRefusalCode" varchar(40) NULL);
CREATE TABLE "SourceRefusals"("Id" uuid PRIMARY KEY, "CreatedAt" timestamptz NOT NULL, "OwnerUsername" text NOT NULL,
  "Path" varchar(16) NOT NULL, "Code" varchar(40) NOT NULL, "Kind" varchar(16) NULL, "Extension" varchar(8) NULL,
  "DeclaredType" varchar(100) NULL, "SizeBytes" bigint NULL);
"""


def _admin(sql: str) -> None:
    cmd = ["docker", "exec", "-i", CONTAINER, "sh", "-c",
           'exec psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -f -']
    done = subprocess.run(cmd, input=sql, capture_output=True, text=True, timeout=60)
    assert done.returncode == 0, done.stderr


def _q(value) -> str:
    if value is None:
        return "NULL"
    if isinstance(value, int):
        return str(value)
    return "'" + str(value).replace("'", "''") + "'"


def load(world) -> None:
    rows = [SCHEMA]
    for sid, s in world.sources.items():
        rows.append(f'INSERT INTO "Sources" VALUES ({_q(sid)}, {_q(s["owner"])}, \'synthetic.pdf\', {_q(s["kind"])}, {_q(s["sha"])}, '
                    f'{s["char_count"]}, {_q(s["origin"])}, \'synthetic text\', {_q(s["created_at"])});')
    for oid, owner in world.orders.items():
        rows.append(f'INSERT INTO "Orders" VALUES ({_q(oid)}, {_q(owner)}, \'synthetic\');')
    for oid, sid in world.order_sources:
        rows.append(f'INSERT INTO "OrderSources" VALUES ({_q(oid)}, {_q(sid)});')
    for rid, r in world.runs.items():
        rows.append(f'INSERT INTO "Runs" VALUES ({_q(rid)}, {_q(r["order"])}, {_q(r["status"])});')
    for n, u in enumerate(world.usages):
        rows.append(f'INSERT INTO "RunSourceUsages" VALUES (gen_random_uuid(), {_q(u["run"])}, {_q(u["source"])}, \'synthetic.pdf\', '
                    f'{_q(u["mode"])}, {_q(u["target"])}, {u["chars"]});')
    for tid, t in world.tickets.items():
        rows.append(f'INSERT INTO "UploadTickets" ("Id", "OwnerUsername", "FileName", "TokenHash", "State", "Attempts", "SourceId", '
                    f'"CreatedAt", "TokenExpiresAt", "ExpiresAt", "LastRefusalCode") VALUES ({_q(tid)}, {_q(t["owner"])}, '
                    f'\'synthetic.pdf\', repeat(\'a\', 64), {_q(t["state"])}, {t.get("attempts", 0)}, {_q(t.get("source_id"))}, '
                    f'now(), now(), now(), {_q(t.get("last_refusal_code"))});')
    for r in world.refusals:
        rows.append(f'INSERT INTO "SourceRefusals" VALUES ({_q(r["Id"])}, {_q(r["CreatedAt"])}, {_q(r["OwnerUsername"])}, '
                    f'{_q(r["Path"])}, {_q(r["Code"])}, {_q(r["Kind"])}, {_q(r["Extension"])}, {_q(r["DeclaredType"])}, '
                    f'{_q(r["SizeBytes"])});')
    _admin("\n".join(rows))


@pytest.fixture(scope="module", autouse=True)
def ready():
    for _ in range(60):
        if subprocess.run(["docker", "exec", CONTAINER, "pg_isready", "-q"], capture_output=True).returncode == 0:
            return
        time.sleep(1)
    pytest.fail("Postgres nicht bereit")


@pytest.fixture
def s(tmp_path):
    scenario = Scenario(tmp_path / "run")
    load(scenario.world)
    return scenario


def _normal_window(value: dict) -> dict:
    value = dict(value)
    value["refusals"] = [dict(r, CreatedAt=datetime.fromisoformat(r["CreatedAt"]).isoformat()) for r in value["refusals"]]
    return value


def test_real_queries_answer_like_the_fake(s):
    real, fake = uc.PsqlDatabase(CONTAINER), s.db()
    orders = {s.ids["u_s1"]: s.ids["o_s1"], s.ids["u_s2"]: s.ids["o_s2"]}
    for upload in s.world.tickets:
        for order in (orders.get(upload), None, s.ids["o_foreign"]):
            for local in (s.world.sources.get(s.world.tickets[upload].get("source_id") or "", {}).get("sha", "b" * 64), "c" * 64):
                assert real.ticket(upload, local, order) == fake.ticket(upload, local, order), (upload, order)
    assert real.ticket("00000000-0000-4000-8000-000000000999", "a" * 64, None) is None
    for gate in uc.WINDOW_GATES:
        start, end = s.marks[gate]["start"], s.marks[gate]["end"]
        names = ["scan-0000.pdf", s.file("scan").name]
        assert _normal_window(real.window(start, end, names)) == _normal_window(fake.window(start, end, names))
    datetime.fromisoformat(real.now())


def test_whole_dry_run_passes_and_fails_on_the_real_database(s):
    real = uc.PsqlDatabase(CONTAINER)
    report = uc.evaluate(s.plan, s.root, real, s.marks)
    assert report["verdict"] == "passed", report
    s.world.sources[s.ids["src_s1"]]["sha"] = "0" * 64
    s.world.refusals.append(dict(s.world.refusals[0], Id="00000000-0000-4000-8000-0000000000aa"))
    load(s.world)
    report = uc.evaluate(s.plan, s.root, real, s.marks)
    assert report["gates"]["T-A2"]["parts"]["S1.d_bytes_equal"]["state"] == uc.NO
    assert report["gates"]["T-A4"]["parts"]["c_rows_exact"]["state"] == uc.NO


def test_owner_with_a_file_name_is_seen_without_returning_the_owner(s):
    row = next(r for r in s.world.refusals if r["Path"] == "upload_page")
    row["OwnerUsername"] = s.file("scan").stem
    load(s.world)
    window = uc.PsqlDatabase(CONTAINER).window(s.marks["T-A4"]["start"], s.marks["T-A4"]["end"], [s.file("scan").stem])
    assert window["owner_has_name"] == 1
    assert all("OwnerUsername" not in r and "Id" not in r for r in window["refusals"])


@pytest.mark.parametrize("sql", ["INSERT INTO \"Orders\" VALUES (gen_random_uuid(), 'x', 'y')", "DELETE FROM \"Sources\"",
                                 "CREATE TABLE probe(x int)", "UPDATE \"UploadTickets\" SET \"State\" = 'Used'"])
def test_no_write_gets_through(s, sql):
    with pytest.raises(uc.DatabaseError):
        uc.PsqlDatabase(CONTAINER).query(sql)
    assert uc.PsqlDatabase(CONTAINER).query('SELECT json_build_object(\'n\', (SELECT count(*) FROM "Sources"))') == {"n": len(s.world.sources)}
