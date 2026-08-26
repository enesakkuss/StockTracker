#!/usr/bin/env bash
set -euo pipefail

# ==============================================================================
# Stock Tracker — SQLite Hot-Backup Script (Zero Locking)
# ==============================================================================

DB_PATH="${1:-/var/data/stocktracker.db}"
BACKUP_DIR="${2:-/var/backups/stocktracker}"
RETENTION_DAYS=14

mkdir -p "${BACKUP_DIR}"

if [ ! -f "${DB_PATH}" ]; then
    echo "Error: Database file not found at ${DB_PATH}"
    exit 1
fi

TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_FILE="${BACKUP_DIR}/stocktracker_backup_${TIMESTAMP}.db"

# Perform online non-blocking SQLite hot backup
sqlite3 "${DB_PATH}" ".backup '${BACKUP_FILE}'"

# Restrict permissions (sensitive user & monitor data)
chmod 600 "${BACKUP_FILE}"

echo "✅ Backup successfully created at: ${BACKUP_FILE}"

# Cleanup backups older than retention days
find "${BACKUP_DIR}" -name "stocktracker_backup_*.db" -mtime +${RETENTION_DAYS} -exec rm -f {} \;
echo "Cleaned backups older than ${RETENTION_DAYS} days."
