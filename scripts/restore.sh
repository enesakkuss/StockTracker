#!/usr/bin/env bash
set -euo pipefail

# ==============================================================================
# Stock Tracker — SQLite Restore Script
# ==============================================================================

BACKUP_FILE="${1:?Usage: ./restore.sh <path_to_backup.db>}"
TARGET_DB="${2:-/var/data/stocktracker.db}"

if [ ! -f "${BACKUP_FILE}" ]; then
    echo "Error: Backup file ${BACKUP_FILE} does not exist."
    exit 1
fi

echo "⚠️  Restoring database from ${BACKUP_FILE} to ${TARGET_DB}"
echo "Stopping application service first..."

if command -v systemctl &> /dev/null; then
    sudo systemctl stop stocktracker || true
fi

# Take safety copy of existing DB before overwrite
if [ -f "${TARGET_DB}" ]; then
    cp "${TARGET_DB}" "${TARGET_DB}.pre_restore_$(date +%s)"
fi

# Restore file
cp "${BACKUP_FILE}" "${TARGET_DB}"
chmod 640 "${TARGET_DB}"

# Remove stale WAL/SHM files to prevent inconsistency
rm -f "${TARGET_DB}-wal" "${TARGET_DB}-shm"

if command -v systemctl &> /dev/null; then
    sudo systemctl start stocktracker
fi

echo "✅ Restore completed and service restarted successfully."
