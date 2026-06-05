$ErrorActionPreference = "Stop"

Write-Warning "=========================================================================="
Write-Warning " WARNING: dry-run-phase1.ps1 is deprecated and intentionally disabled."
Write-Warning " - It was only used during the completed SQL Server to PostgreSQL migration."
Write-Warning " - SQL Server migration tooling is no longer part of the active runtime path."
Write-Warning "=========================================================================="

throw "dry-run-phase1.ps1 is legacy and no longer supported."
