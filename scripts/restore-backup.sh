#!/usr/bin/env bash
# Restore a database backup into the production Postgres container.
# WARNING: This overwrites all existing data in the database. Stop the app first.
# Usage: scripts/restore-backup.sh <path-to-backup-file.sql.gz>

set -euo pipefail

BACKUP_FILE="${1:?Usage: $0 <backup-file.sql.gz>}"

if [ ! -f "$BACKUP_FILE" ]; then
    echo "ERROR: Backup file not found: $BACKUP_FILE"
    exit 1
fi

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
gunzip -c "$BACKUP_FILE" | docker compose exec -T postgres \
    psql -U "${POSTGRES_USER:-geef_atelier}" -d "${POSTGRES_DB:-geef_atelier}"

echo "Restarting app container..."
docker compose start web

echo "Restore complete. Verify the app at https://geef.stefan-bechtel.de/"
