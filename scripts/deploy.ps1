<#
.SYNOPSIS
    Stock Tracker Production Windows/Cross-Platform Deployment Script
#>
param (
    [string]$PublishDir = "./publish",
    [string]$TargetDir = "C:\inetpub\stocktracker",
    [string]$DataDir = "C:\ProgramData\StockTracker"
)

$ErrorActionPreference = "Stop"

Write-Host "=== [1/5] Running Tests in Release Mode ===" -ForegroundColor Cyan
dotnet test StockTracker.sln --configuration Release

Write-Host "=== [2/5] Building Production Publish Artifacts ===" -ForegroundColor Cyan
dotnet publish src\StockTracker.Api\StockTracker.Api.csproj -c Release -o $PublishDir /p:UseAppHost=false

Write-Host "=== [3/5] Backing up SQLite Database ===" -ForegroundColor Cyan
$BackupDir = Join-Path $DataDir "backups"
if (!(Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null }

$DbFile = Join-Path $DataDir "stocktracker.db"
if (Test-Path $DbFile) {
    $Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $BackupFile = Join-Path $BackupDir "stocktracker_backup_$Timestamp.db"
    Copy-Item $DbFile $BackupFile
    Write-Host "Database backed up to $BackupFile" -ForegroundColor Green
}

Write-Host "=== [4/5] Copying Publish Files ===" -ForegroundColor Cyan
if (!(Test-Path $TargetDir)) { New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null }
Copy-Item "$PublishDir\*" $TargetDir -Recurse -Force -Exclude "*.db","*.db-wal","*.db-shm"

Write-Host "=== [5/5] Deployment Complete ===" -ForegroundColor Green
