#!/usr/bin/env bash
# Restore a database backup into the production Postgres container.
# WARNING: This replaces all existing data in the database. The script stops the app first.
# The schema `public` is dropped and rebuilt from the dump in one transaction: if the dump cannot be applied completely,
# nothing changes, the script ends with an error and the app stays stopped.
# Usage: scripts/restore-backup.sh <path-to-backup-file.sql.gz>   (from the repository root)

set -euo pipefail

BACKUP_FILE="${1:?Usage: $0 <backup-file.sql.gz>}"

if [ ! -f "$BACKUP_FILE" ]; then
    echo "ERROR: Backup file not found: $BACKUP_FILE"
    exit 1
fi

# A truncated file would otherwise be committed as far as it goes.
if ! gunzip -t "$BACKUP_FILE" || [ "$(gunzip -c "$BACKUP_FILE" | grep -c 'PostgreSQL database dump complete')" -eq 0 ]; then
    echo "ERROR: Not a complete gzip-compressed SQL dump: $BACKUP_FILE"
    exit 1
fi
HAS_CREATE_SCHEMA="$(gunzip -c "$BACKUP_FILE" | grep -c '^CREATE SCHEMA public;' || true)"

# Load credentials from .env if present
if [ -f .env ]; then
    set -a
    # shellcheck source=/dev/null
    source .env
    set +a
fi

echo "Restoring $BACKUP_FILE into database '${POSTGRES_DB:-geef_atelier}'..."
echo "Stopping app container..."
docker compose stop web

echo "Running restore via psql..."
{
    echo 'SET client_min_messages = warning;'
    echo 'DROP SCHEMA IF EXISTS public CASCADE;'
    if [ "$HAS_CREATE_SCHEMA" -eq 0 ]; then echo 'CREATE SCHEMA public;'; fi
    gunzip -c "$BACKUP_FILE"
} | docker compose exec -T postgres \
    psql -v ON_ERROR_STOP=1 --single-transaction --quiet \
    -U "${POSTGRES_USER:-geef_atelier}" -d "${POSTGRES_DB:-geef_atelier}" > /dev/null

echo "Restarting app container..."
docker compose start web

echo "Restore complete. Verify the app at https://${ATELIER_DOMAIN:-<your-domain>}/health"
