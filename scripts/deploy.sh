#!/usr/bin/env bash
set -euo pipefail

# ==============================================================================
# Stock Tracker — Production Zero-Downtime / Controlled Deployment Script
# ==============================================================================

APP_DIR="/var/www/stocktracker"
DATA_DIR="/var/data"
BACKUP_DIR="/var/backups/stocktracker"
PUBLISH_DIR="./publish"

echo "=== [1/6] Running Tests in Release Mode ==="
dotnet test StockTracker.sln --configuration Release

echo "=== [2/6] Building Production Publish Artifacts ==="
dotnet publish src/StockTracker.Api/StockTracker.Api.csproj -c Release -o "${PUBLISH_DIR}" /p:UseAppHost=false

echo "=== [3/6] Taking Pre-Deployment SQLite Backup ==="
mkdir -p "${BACKUP_DIR}"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
if [ -f "${DATA_DIR}/stocktracker.db" ]; then
    sqlite3 "${DATA_DIR}/stocktracker.db" ".backup '${BACKUP_DIR}/stocktracker_predeploy_${TIMESTAMP}.db'"
    echo "Backup saved to ${BACKUP_DIR}/stocktracker_predeploy_${TIMESTAMP}.db"
fi

echo "=== [4/6] Syncing Publish Files to App Directory ==="
mkdir -p "${APP_DIR}"
rsync -av --delete --exclude='*.db' --exclude='*.db-wal' --exclude='*.db-shm' "${PUBLISH_DIR}/" "${APP_DIR}/"

echo "=== [5/6] Restarting StockTracker Service ==="
if command -v systemctl &> /dev/null; then
    sudo systemctl restart stocktracker
elif command -v docker &> /dev/null && [ -f "docker-compose.prod.yml" ]; then
    docker compose -f docker-compose.prod.yml restart stocktracker-api
fi

echo "=== [6/6] Running Health Check Verification ==="
sleep 3
HEALTH_STATUS=$(curl -s -o /dev/null -w "%{http_code}" http://127.0.0.1:5000/health/ready || echo "000")

if [ "${HEALTH_STATUS}" -eq 200 ]; then
    echo "✅ Deployment Successful! Service is healthy on http://127.0.0.1:5000"
else
    echo "❌ Health check failed with status: ${HEALTH_STATUS}. Check logs immediately."
    exit 1
fi
