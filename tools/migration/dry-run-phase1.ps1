param(
    [string]$SourceServer = ".",
    [string]$SourceDatabase = "UniMap360_Pro",
    [string]$TargetConnectionString = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $root

Write-Host "== UniMap360 Phase 1 Dry-Run ==" -ForegroundColor Cyan
Write-Host "Workspace: $root"

$planDir = Join-Path $root ".Agent\Plan"
if (-not (Test-Path $planDir)) {
    New-Item -ItemType Directory -Path $planDir | Out-Null
}

$schemaScript = Join-Path $planDir "PostgreSql_Init_Script_Phase1.sql"
$sourceCountsCsv = Join-Path $planDir "DryRun_Source_RowCounts.csv"

Write-Host ""
Write-Host "[1/4] Generate PostgreSQL migration SQL script..." -ForegroundColor Yellow
$env:ConnectionStrings__DefaultConnection = if ([string]::IsNullOrWhiteSpace($TargetConnectionString)) {
    "Host=localhost;Port=5432;Database=unimap360_tmp;Username=postgres;Password=postgres"
} else {
    $TargetConnectionString
}

dotnet ef migrations script `
    --context UniMap360PostgresContext `
    --output $schemaScript | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Failed to generate PostgreSQL migration script."
}

Write-Host "Schema script generated: $schemaScript" -ForegroundColor Green

Write-Host ""
Write-Host "[2/4] Probe SQL Server source connection..." -ForegroundColor Yellow
try {
    sqlcmd -S $SourceServer -E -d $SourceDatabase -C -Q "SELECT 1" -W -h -1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "SQL source connection test failed." }
    Write-Host "Source SQL Server is reachable: $SourceServer / $SourceDatabase" -ForegroundColor Green
}
catch {
    Write-Warning "Source SQL Server is NOT reachable with current params."
    Write-Warning $_.Exception.Message
    Write-Host "Tips: check SQL Server service, instance name, or run with -SourceServer." -ForegroundColor DarkYellow
    exit 2
}

Write-Host ""
Write-Host "[3/4] Export source row-count baseline..." -ForegroundColor Yellow
$countQuery = @"
SET NOCOUNT ON;
SELECT 'TaiKhoan' AS TableName, COUNT_BIG(1) AS RowCount FROM TaiKhoan
UNION ALL SELECT 'HoSoSinhVien', COUNT_BIG(1) FROM HoSoSinhVien
UNION ALL SELECT 'HoSoChuTro', COUNT_BIG(1) FROM HoSoChuTro
UNION ALL SELECT 'HoSoNhaTuyenDung', COUNT_BIG(1) FROM HoSoNhaTuyenDung
UNION ALL SELECT 'DiaDiem', COUNT_BIG(1) FROM DiaDiem
UNION ALL SELECT 'PhongTro', COUNT_BIG(1) FROM PhongTro
UNION ALL SELECT 'ViecLam', COUNT_BIG(1) FROM ViecLam
UNION ALL SELECT 'RoommatePost', COUNT_BIG(1) FROM RoommatePost
UNION ALL SELECT 'TepDaPhuongTien', COUNT_BIG(1) FROM TepDaPhuongTien
UNION ALL SELECT 'ThongBao', COUNT_BIG(1) FROM ThongBao
UNION ALL SELECT 'CuocTroChuyen', COUNT_BIG(1) FROM CuocTroChuyen
UNION ALL SELECT 'TinNhan', COUNT_BIG(1) FROM TinNhan
UNION ALL SELECT 'LichXemPhong', COUNT_BIG(1) FROM LichXemPhong
UNION ALL SELECT 'HoSoUngTuyen', COUNT_BIG(1) FROM HoSoUngTuyen
UNION ALL SELECT 'DanhGia', COUNT_BIG(1) FROM DanhGia;
"@

sqlcmd -S $SourceServer -E -d $SourceDatabase -C -W -s "," -Q $countQuery | Set-Content -Encoding UTF8 $sourceCountsCsv
if ($LASTEXITCODE -ne 0) {
    throw "Failed to export source row counts."
}
Write-Host "Source baseline exported: $sourceCountsCsv" -ForegroundColor Green

Write-Host ""
Write-Host "[4/4] PostgreSQL apply dry-run guidance..." -ForegroundColor Yellow
if ([string]::IsNullOrWhiteSpace($TargetConnectionString)) {
    Write-Warning "TargetConnectionString is empty, so migration was NOT applied."
    Write-Host "Run again with -TargetConnectionString to apply migration to Supabase/PostgreSQL." -ForegroundColor DarkYellow
    exit 3
}

Write-Host "Applying PostgreSQL migration..." -ForegroundColor Yellow
dotnet ef database update `
    --context UniMap360PostgresContext `
    --connection $TargetConnectionString | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Failed to apply PostgreSQL migration on target."
}

Write-Host ""
Write-Host "Dry-run completed successfully." -ForegroundColor Green
Write-Host "Next: compare target row counts (after import) with $sourceCountsCsv."
