$ErrorActionPreference = "Stop"

Write-Warning "=========================================================================="
Write-Warning " WARNING: reset-and-seed.ps1 is deprecated and intentionally disabled."
Write-Warning " - The project now uses PostgreSQL/Supabase."
Write-Warning " - Authentication now uses HttpOnly cookies instead of JWT response bodies."
Write-Warning " - Use application seeding endpoints or PostgreSQL-compatible tooling instead."
Write-Warning "=========================================================================="

throw "reset-and-seed.ps1 is legacy and no longer supported."
