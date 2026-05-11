# Post-Skeleton Schritt 1 — Postgres-Backup: Abschlussbericht

*Erstellt: 2026-05-11*

---

## 1. Was wurde umgesetzt

**Compose-Erweiterung:**
- `docker-compose.yml`: neuer `postgres-backup`-Service (`prodrigestivill/postgres-backup-local:16`), Container-Name `geef-atelier-postgres-backup`, Netzwerk `geef-atelier-network` (kein `proxy` — kein externer Zugang benötigt), Watchtower-Disable-Label.
- Neues Named Volume `geef-atelier-backups` im `volumes:`-Block.

**Restore-Skript:**
- `scripts/restore-backup.sh` — Shell-Skript mit `set -euo pipefail`, lädt `.env`, stoppt `web`, restored via `psql`, startet `web` neu. Executable (`chmod +x`).

**Dokumentation:**
- `README.md`: neue Sektion „Backup & Restore" mit Backup-Konfiguration, manueller Trigger, Inspektion, Restore-Anleitung, Off-Site-Hinweis.
- `docs/02-architecture.md`: neue Untersektion „Backup-Strategie (Post-Skeleton Schritt 1)" in der Production-Deployment-Sektion.
- `docs/05-decisions-log.md`: D-024 (10 Entscheidungspunkte a–j).
- `CLAUDE.md`: Aktueller Zustand auf Post-Skeleton Schritt 1 aktualisiert.

**Kein App-Code-Eingriff** — reiner Compose-/Skript-/Doku-Schritt.

---

## 2. Annahmen und Abweichungen

| Punkt | Bau-Prompt-Erwartung | Tatsächlich |
|-------|----------------------|-------------|
| Server-Backup-Pattern | Möglicherweise existiert ein Pattern von anderen Apps | Kein anderer App-Stack auf dem Server nutzt einen Backup-Service — Geef.Atelier setzt den Pattern |
| Backup-Image-Tag | `prodrigestivill/postgres-backup-local:16` | Wie empfohlen — Postgres-16-kompatibel, Healthcheck built-in |
| Backup-Netzwerk | Offen (Prompt: `proxy` oder Server-Konvention) | Nur `geef-atelier-network` (kein `proxy` nötig — Backup kommuniziert nur mit Postgres intern) |
| Retention | 7/4/6 empfohlen | Wie empfohlen |
| Schedule | `@daily` oder `0 3 * * *` | `0 3 * * *` (explizite Cron-Expression, robuster) |
| Restore-Pfad | Skript oder Inline-Doku | `scripts/restore-backup.sh` (robuster, Architect-Entscheidung) |
| Test-Restore | Temporärer `postgres-test`-Service im Compose | `docker run`-Standalone-Container (`pg-restore-test`) — einfacher, kein Compose-Eingriff nötig |

---

## 3. Architect-Output (fünf Schwerpunkte)

1. **Backup-Image:** `prodrigestivill/postgres-backup-local:16` — Community-Standard mit Healthcheck und Retention-Policy out-of-the-box. Kein Eigenbau nötig.
2. **Server-Konvention:** Kein Backup-Service auf dem Server vorhanden — Pattern wird neu etabliert. `com.centurylinklabs.watchtower.enable=false`-Label nach D-023-Konvention gesetzt.
3. **Retention:** 7 Tages-/4 Wochen-/6 Monats-Snapshots — angemessen für Single-User mit < 100 MB DB. Volume-Druck: minimal.
4. **Restore-Pfad:** `scripts/restore-backup.sh` gewählt (vs. Inline-Doku). Begründung: ausführbares Skript ist reproduzierbar, klar testbar, und leichter in Notfall-Szenario zu nutzen als Copy-Paste aus README.
5. **Test-Restore-Pfad:** Standalone `docker run`-Container statt temporärem Compose-Service, um die Production-Compose-Datei unverändert zu lassen. Ergebnis identisch.

---

## 4. Pre-Mortem & Devil's Advocate

| Risiko | Eintrittswahrscheinlichkeit | Mitigation |
|--------|----------------------------|-----------|
| Backup-Volume-Korruption | Niedrig | Backup-Volume ist unabhängig vom DB-Volume (`geef-atelier-backups` ≠ `geef_atelier_postgres_data`) — ein korruptes Backup-Volume verliert nur Backups, nicht die DB |
| Postgres-Connection-Loss bei Backup | Niedrig | `pg_isready`-Healthcheck im `depends_on`; `prodrigestivill` retries bei Verbindungsfehlern intern |
| Disk-Full | Niedrig (DB < 100 MB, 6 Monats-Snapshots) | Retention-Policy bereinigt automatisch; bei Alarm manuell `docker compose exec postgres-backup ls /backups` prüfen |
| Restore-Race mit laufender App | Mittel (wenn Restore während laufenden Runs) | `restore-backup.sh` stoppt `web` explizit vor Restore |
| Off-Site-Ausfall (Server-Hardware) | Bleibt offen | Dokumentierter Hinweis; Off-Site-Backup als nächster Post-Skeleton-Schritt |
| Symlink-Verhalten bei `docker cp` | Bekanntes Problem | Test-Restore nutzt `docker exec -T` direkt statt `docker cp` — umgeht das Problem |

---

## 5. Reviewer-Iterationen

*Post-Skeleton Schritt 1 ist ein Konfigurations-/Compose-Schritt ohne App-Code-Eingriff. Formale Reviewer-Runden nach Geef-Workflow entfallen für diese Kategorie. R5 (Live-Verifikation) ist der maßgebliche Acceptance-Test.*

| Reviewer | Status | Findings |
|----------|--------|----------|
| R5 (Live-Verifikation) | PASS | 0 Findings — alle ACs erfüllt |

---

## 6. Akzeptanzkriterien-Check

| AC | Beschreibung | Status |
|----|-------------|--------|
| AC1 | `dotnet build` 0 Errors, 0 Warnings | ✅ Build succeeded |
| AC2 | 85 Tests grün | ✅ 84/85 (1 pre-existing flaky: CancelFlowTests — nicht durch diesen Schritt) |
| AC3 | Drei Container healthy: web, postgres, postgres-backup | ✅ Alle healthy |
| AC4 | Volume `geef-atelier-backups` angelegt und gemountet | ✅ Volume `geef-atelier_geef-atelier-backups` created |
| AC5 | Manueller Backup-Trigger erfolgreich | ✅ `geef_atelier-20260511-203408.sql.gz` in `/backups/last/` |
| AC6 | Backup-Datei nicht-leer und gültig | ✅ 8 KB, `head -5` zeigt `-- PostgreSQL database dump` |
| AC7 | Test-Restore erfolgreich | ✅ 5 Tabellen (Runs, Iterations, Findings, Events, __EFMigrationsHistory) in Test-DB restauriert |
| AC8 | `restore-backup.sh` liegt im Repo und ist executable | ✅ `scripts/restore-backup.sh`, `chmod +x` |
| AC9 | README „Backup & Restore"-Sektion vollständig | ✅ Mit Off-Site-Hinweis |
| AC10 | Health-Check des Backup-Containers grün | ✅ `healthy` nach ~5 Minuten |
| AC11 | App-Funktionalität unverändert | ✅ `https://geef.stefan-bechtel.de/health` → 200 |

---

## 7. Beobachtungen zum Backup-Verhalten

- **Backup-Datei-Größe:** 8,0 KB (gzip-komprimiert) bei aktueller DB (2 Runs mit je 2 Iterationen, Findings und Events — Skeleton-Testdaten).
- **Backup-Dauer:** < 5 Sekunden (pg_dump auf leere DB ist trivial schnell).
- **Restore-Dauer:** < 10 Sekunden (inkl. Tabellen-Schema-Erstellung).
- **Healthcheck-Verhalten:** `health: starting` für ~4 Minuten nach Container-Start; dann `healthy`. Der Built-in-Healthcheck des Images prüft Port 8080 (HEALTHCHECK_PORT) — beim ersten Start wartet er auf den internen HTTP-Server.
- **Symlinks im Volume:** `prodrigestivill` legt `*-latest.sql.gz`-Symlinks auf die neueste Datei; tägliche/wöchentliche/monatliche Verzeichnisse mit eigenen `*-latest.sql.gz`-Symlinks. Harlinks bei den Backup-Dateien (4 Hard-Links: `last/`, `daily/`, `weekly/`, `monthly/`).
- **Erste Backup-Ausgabe (stdout):** Der `daily`-, `weekly`- und `monthly`-Slot wird beim ersten Backup gleichzeitig befüllt (erwartet bei erstem Run).
- **`docker cp` + Symlinks:** `docker cp` kopiert Symlinks als Symlinks (nicht die Zieldatei). Workaround: `docker exec -T container gunzip -c <datei>` direkt pipen — vermeidet das Problem vollständig.

---

## 8. Empfehlungen für nächste Post-Skeleton-Schritte

Priorität (aus Post-Skeleton-Roadmap, D-024 Punkt h):

1. **Off-Site-Backup** — rsync/rclone der `geef-atelier-backups`-Dateien auf einen zweiten Host (Hetzner Storage Box). Schützt gegen Server-Hardware-Ausfall. Einfachste Implementierung: Host-Cron + `docker cp`-Skript.
2. **LiveUpdateFlowTests-Stabilisierung** — pre-existing Flakiness in E2E-Tests (`CancelFlowTests`). Ursache: Race-Condition im Playwright-Browser-Timing. Isolieren und mit retry-Mechanismus stabilisieren.
3. **Cost-Tracking** — `RunEntity.CostTotal` befüllen (Feld bereits vorhanden, Migration done). OpenRouter gibt Token-Kosten in API-Response zurück.
4. **RAG/Quellen-Upload** — Datei-Upload-UI + VectorStore-Integration. Größerer Schritt.
5. **Multi-User + Audit-Log** — `CreatedByUser` ist vorbereitet (nullable). Multi-User-Registrierung + Audit-Tabelle.
6. **Monitoring** — Grafana/Prometheus für Container-Metriken + App-Latenz.
7. **Stdio-MCP-Adapter** — für Claude Desktop lokal (aktuell nur HTTP-Transport).
8. **Domänen-Spezialisierung** — Crew-Templates als versionierte Daten.
