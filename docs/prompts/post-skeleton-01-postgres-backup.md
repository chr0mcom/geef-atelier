# Claude-Code-Prompt: Post-Skeleton Schritt 1 — Postgres-Backup

*Diese Datei ist als Eingabe für Claude Code gedacht. Erster Schritt nach Walking-Skeleton-Abschluss.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Der Walking Skeleton (10 Schritte + M1) ist abgeschlossen. Das System läuft produktiv unter `https://geef.stefan-bechtel.de/` mit Cookie-Auth-UI, Bearer-Auth-MCP-Endpoint und OpenRouter als LLM-Provider. 85/85 Tests grün. Aktuell **fehlt jedoch eine Backup-Strategie für die Postgres-Daten** — Datenverlust bei Container-Crash oder Volume-Korruption würde alle Run-Daten unwiederbringlich vernichten.

Deine Aufgabe ist **Post-Skeleton Schritt 1**: Aufbau eines automatisierten Postgres-Backup-Systems im bestehenden Docker-Stack, mit Retention-Policy, Restore-Anleitung und einmaligem Test-Restore zur Verifikation.

Was sich ändert: Erweiterung der `docker-compose.yml` um einen Backup-Service mit eigenem Volume. README bekommt eine Backup-/Restore-Sektion. Was bleibt unverändert: Anwendungs-Code, Domain-Modell, Tests, Provider-Schichten, beide Frontends. Keine Migration. Keine neue `.env`-Variable (Backup nutzt die bestehende Postgres-Credentials).

Dies ist ein **Container-Erweiterungs-Schritt**, kein Code-Schritt. Der App-Container selbst wird nicht angefasst.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, alle Hard Rules. **Plan-Phase-Integration** ist Standard (seit Schritt 5, siehe D-016 bis D-023) — Architect-Antworten direkt im Plan-Dokument fixieren, kein separater `claude -p`-Aufruf.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`/srv/CLAUDE.md`** und (falls existent) **`/srv/docker/docs/docker-deployment.md`** — **kritisch**, weil die Server-Konvention für Volumes, Backup-Patterns und ggf. existierende Backup-Services für andere Apps maßgeblich ist. Hat ein anderer Service auf diesem Server bereits ein Backup-Setup, das wir als Vorlage nehmen können?
3. **`CLAUDE.md`** im Repo-Root.
4. **`docs/02-architecture.md`** — besonders Persistierungs- und Production-Deployment-Sektionen.
5. **`docs/05-decisions-log.md`**, insbesondere **D-023** (Production-Compose-Strategie mit eigenem Postgres-Container).
6. **`docs/reports/step-10-report.md`**, besonders **Sektion 9 (Post-Skeleton-Roadmap)** mit Backup als Punkt 1 priorisiert.
7. **Aktueller Repo-Stand:**
   - `docker-compose.yml` — Production-Compose aus Schritt 10. Hier kommt der Backup-Service rein.
   - `.env` (lokal, nicht im Repo) — enthält `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`. Backup-Service muss dieselben Credentials nutzen.
   - `README.md` — bekommt eine "Backup & Restore"-Sektion.
8. **`prodrigestivill/postgres-backup-local`-Doku** auf Docker Hub: [`https://hub.docker.com/r/prodrigestivill/postgres-backup-local`](https://hub.docker.com/r/prodrigestivill/postgres-backup-local). Verstehe Env-Vars (`POSTGRES_HOST`, `BACKUP_KEEP_DAYS`, `BACKUP_KEEP_WEEKS`, `BACKUP_KEEP_MONTHS`, `SCHEDULE`, `HEALTHCHECK_PORT`).
9. **PostgreSQL `pg_dump`/`pg_restore`-Doku** für das Restore-Verfahren.

## In Schritten 1–10 etablierte Realfakten (verbindlich)

Aus D-010 bis D-023. Zentrale Punkte für diesen Schritt:

**Compose-Stack (aus D-023):**
- Eigener `postgres:16-alpine`-Container im Stack (nicht Server-Postgres).
- Netzwerk `proxy` (Server-Konvention, nicht `traefik`).
- `pull_policy: never`, Watchtower-Disable.
- Postgres-Volume: `geef-atelier-pgdata` als Named Volume.
- Postgres-Credentials via `${POSTGRES_USER}`/`${POSTGRES_PASSWORD}` aus `.env`.

**Server-Konvention:**
- Cert-Resolver heißt `le`, nicht `letsencrypt`.
- `chain@file`-Middleware wird genutzt.
- Architect-Aufgabe: **immer Server-Konvention prüfen**, nicht idiomatische Form annehmen (D-023 hat diese Disziplin etabliert).

**Daten-Volumen-Erwartung:**
- Single-User, derzeit handful Test-Runs in der DB.
- Pro Run: ~5-20 Iterations, ~10-50 Findings, plus Events. Geschätzt < 1 MB pro Run.
- Skeleton-DB-Größe: wenige MB. Erwartet auch nach erstem Real-Use-Case-Briefing < 100 MB.
- Backup-Datei-Größe entsprechend klein — kein Performance- oder Storage-Druck.

## Konkrete technische Anforderungen

### 1. Backup-Service in `docker-compose.yml`

Empfehlung: `prodrigestivill/postgres-backup-local:16` (Postgres-16-kompatibel). Beispiel-Block:

```yaml
  postgres-backup:
    image: prodrigestivill/postgres-backup-local:16
    container_name: geef-atelier-postgres-backup
    restart: unless-stopped
    networks:
      - proxy  # oder Server-Konvention
    depends_on:
      - postgres
    environment:
      POSTGRES_HOST: postgres
      POSTGRES_DB: ${POSTGRES_DB}
      POSTGRES_USER: ${POSTGRES_USER}
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
      POSTGRES_EXTRA_OPTS: "-Z6 --schema=public --blobs"
      SCHEDULE: "@daily"  # Cron-Expression, alternativ: "0 3 * * *" für 03:00 UTC
      BACKUP_KEEP_DAYS: 7
      BACKUP_KEEP_WEEKS: 4
      BACKUP_KEEP_MONTHS: 6
      HEALTHCHECK_PORT: 8080
    volumes:
      - geef-atelier-backups:/backups
    labels:
      - "com.centurylinklabs.watchtower.enable=false"  # Server-Konvention aus Schritt 10
```

**Volume-Definition** im `volumes:`-Block am Ende der Compose-Datei:
```yaml
volumes:
  geef-atelier-pgdata:
  geef-atelier-backups:  # neu
```

**Architect-Aufgaben:**
- Verifizieren, dass `prodrigestivill/postgres-backup-local:16` der richtige Tag für unsere Postgres-Version ist. Falls Postgres-Tag im App-Compose `:16-alpine` ist, sollte der Backup-Tag auch `:16` sein (kein Versionsmismatch).
- Server-Netzwerk-Name prüfen (`proxy` ist Konvention aus D-023).
- `SCHEDULE`-Wert: täglich um 03:00 UTC ist konventionell — Architect kann anpassen, falls Server-Auslastung dagegen spricht.
- Retention-Werte (`BACKUP_KEEP_DAYS=7`, `WEEKS=4`, `MONTHS=6`) — angemessen für Single-User-Skeleton mit < 100 MB DB? Architect kalibriert.

### 2. Restore-Skript (optional aber empfohlen)

Anlegen unter `scripts/restore-backup.sh`:

```bash
#!/usr/bin/env bash
# Restore aus Backup-Datei in die Production-Postgres-DB.
# Usage: scripts/restore-backup.sh <pfad-zur-backup-datei>
# WARNING: Überschreibt bestehende Daten. Stoppe vorher die App.

set -euo pipefail

BACKUP_FILE="${1:?Usage: $0 <backup-file.sql.gz>}"

if [ ! -f "$BACKUP_FILE" ]; then
    echo "ERROR: Backup-Datei nicht gefunden: $BACKUP_FILE"
    exit 1
fi

# .env laden für Credentials
if [ -f .env ]; then
    set -a
    source .env
    set +a
fi

echo "Restore von $BACKUP_FILE in DB '$POSTGRES_DB'..."
echo "Aktuelle App-Container stoppen (falls laufend)..."
docker compose stop geef-atelier

echo "Restore via psql..."
gunzip -c "$BACKUP_FILE" | docker compose exec -T postgres \
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"

echo "App-Container wieder starten..."
docker compose start geef-atelier

echo "Restore abgeschlossen."
```

**Script-Hygiene:**
- `chmod +x scripts/restore-backup.sh`
- Im Header: deutliche WARNUNG, dass das überschreibt
- Erwartet Backup-Datei im `.sql.gz`-Format (das ist das, was `prodrigestivill` schreibt)

Alternativ: Restore-Schritte als Inline-Doku im README, kein Skript. Architect entscheidet.

### 3. README-Update

Neue Sektion **"Backup & Restore"** nach der Production-Deployment-Sektion. Beinhaltet:

**Backup-Konfiguration** (was läuft automatisch):
- Täglicher Backup um 03:00 UTC
- Retention: 7 Tages-Snapshots, 4 Wochen-Snapshots, 6 Monats-Snapshots
- Speicherort: Docker-Volume `geef-atelier-backups`, gemountet auf `/backups` im Backup-Container
- Format: `.sql.gz` (gzip-komprimiertes pg_dump SQL)

**Backup manuell auslösen:**
```bash
docker compose exec postgres-backup /backup.sh
```

**Aktuelle Backups inspizieren:**
```bash
docker compose exec postgres-backup ls -lh /backups/last/
docker compose exec postgres-backup ls -lh /backups/daily/
```

**Restore-Anleitung:**
```bash
# 1. Backup-Datei vom Volume kopieren (Beispiel)
docker cp geef-atelier-postgres-backup:/backups/daily/geef_atelier-2026-05-15.sql.gz ./

# 2. App stoppen (Backup-Container kann weiterlaufen)
docker compose stop geef-atelier

# 3. Restore
./scripts/restore-backup.sh ./geef_atelier-2026-05-15.sql.gz

# 4. Verifikation: App neu starten und auf /runs nach bekannten Run-Details prüfen
docker compose start geef-atelier
```

**Off-Site-Backup-Hinweis:**
Lokales Backup auf demselben Host schützt nur gegen DB-Container-Crash und Logic-Errors, **nicht** gegen Server-Hardware-Ausfall oder versehentliches `docker volume rm`. Für robusteren Schutz: regelmäßiges `docker cp` der Backup-Dateien auf einen anderen Host (z.B. via cron auf einem zweiten Server, oder rsync zu einer Hetzner Storage Box). Das ist Post-Skeleton-Erweiterung Nummer X (Brainstorming-Notiz).

### 4. Einmaliger Test-Restore zur Verifikation

**Pflicht-Schritt im Build:** Nach dem Aufbau des Backup-Services führst du einmalig einen Test-Restore durch, um zu verifizieren, dass der ganze Pfad funktioniert:

1. Backup manuell triggern: `docker compose exec postgres-backup /backup.sh`
2. Backup-Datei finden: `docker compose exec postgres-backup ls -lh /backups/last/`
3. Test-DB im selben Compose-Stack temporär anlegen (z.B. `postgres-test`-Service) und Backup einspielen.
4. Verifizieren, dass die wichtigsten Tabellen (`Runs`, `Iterations`, `Findings`, `Events`, `__EFMigrationsHistory`) in der Test-DB existieren und die richtige Zeilenanzahl haben.
5. Test-Service wieder entfernen.

Alternativ: Restore via `restore-backup.sh` gegen die echte DB. **Vorsicht:** Das überschreibt bestehende Daten. Nur wenn die DB ohnehin leer/Test-Daten enthält, sonst Test-DB-Variante.

Test-Ergebnis im Bericht dokumentieren: Backup-Datei-Größe, Restore-Zeit, Tabellen-Verifikation.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` 0 Errors, 0 Warnings (sollte trivial sein, weil kein Code-Eingriff).
2. `dotnet test` — alle 85 bestehenden Tests grün (sollte trivial sein, weil kein Code-Eingriff).
3. **`docker compose up -d` startet drei Container healthy:** `geef-atelier`, `postgres`, `postgres-backup`.
4. **Backup-Volume `geef-atelier-backups` ist angelegt** und vom Backup-Container gemountet.
5. **Erster manueller Backup-Trigger erfolgreich:** `docker compose exec postgres-backup /backup.sh` schreibt eine `.sql.gz`-Datei nach `/backups/last/`.
6. **Backup-Datei ist nicht-leer und gültig:** `gunzip -c <file> | head -20` zeigt SQL-Header (`-- PostgreSQL database dump`).
7. **Test-Restore erfolgreich:** Backup-Inhalt lässt sich in eine Test-DB einspielen, Tabellen sind vollständig (mit erwarteter Zeilenanzahl).
8. **`restore-backup.sh`-Skript (oder gleichwertige Inline-Doku) liegt im Repo** und ist executable.
9. **README "Backup & Restore"-Sektion ist vollständig** mit allen Befehlen und Off-Site-Hinweis.
10. **Health-Check des Backup-Containers ist grün:** `docker compose ps postgres-backup` zeigt `healthy`-Status.
11. **App-Funktionalität unverändert:** Login, Submit-Flow, MCP-Endpoint funktionieren weiter (curl-Quick-Check reicht — R5 muss nicht den vollen UI-Flow durchgehen, da nichts an der App geändert wurde).

## Was du in diesem Schritt NICHT tust

- **Kein App-Code anfassen** — keine `Program.cs`-Änderung, keine neuen Services in `src/`. Reine Container/Compose-Erweiterung.
- **Keine neuen `.env`-Variablen** — Backup nutzt die bestehenden Postgres-Credentials.
- **Keine Off-Site-Backup-Implementation** — nur Doku-Hinweis. Off-Site (S3, rsync, etc.) kommt als nächste Iteration, wenn überhaupt.
- **Keine Backup-Verschlüsselung** — nicht nötig auf demselben Host, wo die DB sowieso liegt.
- **Keine Monitoring-/Alerting-Integration** — Healthcheck reicht. Grafana/Prometheus ist Post-Skeleton-Roadmap-Punkt 6.
- **Keine automatisierten Restore-Tests** — manuell einmal verifizieren, dann gut. Vollautomatische Restore-Validierung wäre Overkill für Single-User.
- **Keine neuen UI/MCP-Features** — die Backup-Sichtbarkeit kommt nicht in die UI (Single-User braucht das nicht).

## Architect-Konsultation (Phase 1.4) — fünf Schwerpunkte

1. **Backup-Image-Wahl:** `prodrigestivill/postgres-backup-local:16` (Empfehlung) vs. eigener Bau mit `pg_dump`+`cron`-Image vs. anderes Community-Image? `prodrigestivill` ist die etablierte Wahl mit Healthcheck und Retention-Policy out-of-the-box.
2. **Server-Konvention für Backup-Pattern:** Wird auf dem Server bereits ein Backup-Service für eine andere App genutzt? Falls ja: kopieren wir das Pattern. Falls nein: setzen wir den Standard mit `prodrigestivill`.
3. **Retention-Policy:** 7 Tage / 4 Wochen / 6 Monate (Empfehlung) — passt das zur erwarteten Volume-Größe und zur Backup-Disk-Kapazität auf dem Server?
4. **Restore-Pfad:** Skript unter `scripts/restore-backup.sh` (Empfehlung) vs. nur Inline-Doku im README. Skript ist robuster, aber eine Datei mehr im Repo.
5. **Test-Restore-Pfad:** Temporärer `postgres-test`-Service im Compose, oder Restore gegen Produktions-DB (riskanter)? Architect entscheidet, abhängig vom aktuellen Daten-Stand der Production-DB.

`geef_architecture.md` prüft Konsistenz mit dem Persistierungs-Schichtenbild aus `02-architecture.md`.

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/post-skeleton-01-postgres-backup-report.md`, gleicher Aufbau wie die Walking-Skeleton-Berichte. Wichtig in diesem Schritt:

1. **Was wurde umgesetzt** — Compose-Erweiterung, Volumes, ggf. Restore-Skript, README-Sektion.
2. **Annahmen und Abweichungen** — vor allem zur Server-Konvention und zur Image-Wahl.
3. **Architect-Output** — fünf Schwerpunkte als Plan-Phase-Output.
4. **Pre-Mortem & Devil's Advocate** — speziell zu: Backup-Volume-Korruption, Postgres-Connection-Loss bei Backup-Trigger, Disk-Full-Szenario, Restore-Race mit laufender App.
5. **Reviewer-Iterationen** — Tabelle. R5 ist diesmal **Backup-Verifikation**: Backup-Container healthy, manueller Backup-Trigger erfolgreich, Test-Restore in Test-DB erfolgreich.
6. **Akzeptanzkriterien-Check** — Tabelle.
7. **Beobachtungen zum Backup-Verhalten** — Backup-Datei-Größe (vermutlich wenige KB bei aktueller DB), Backup-Dauer, Restore-Dauer, Healthcheck-Interval-Verhalten.
8. **Empfehlungen für nächste Post-Skeleton-Schritte** — kurzer Hinweis, was als nächstes sinnvoll wäre (LiveUpdateFlowTests-Stabilisierung? Cost-Tracking? Off-Site-Backup?).

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- **Niemals Secrets** in source control, Logs, oder Bericht.
- `.gitignore` bleibt unverändert (Backup-Files landen im Volume, nicht im Repo).
- Im Bericht keine konkreten Backup-Datei-Inhalte loggen (selbst wenn nur Test-Run-Daten enthalten sind — Disziplin-Gewohnheit).

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.

---
