# Script to apply migrations to Supabase PostgreSQL
$env:ConnectionStrings__DefaultConnection = "Host=aws-1-ap-southeast-1.pooler.supabase.com;Port=6543;Database=postgres;Username=postgres.tnbkkezvvuecvmwcmknq;Password=tranquyet1812;SSL Mode=Require;Trust Server Certificate=true;Timeout=600;Command Timeout=600;Pooling=false;"
$env:Database__Provider = "PostgreSql"

Write-Host "== Applying UniMap360 Migrations to Supabase ==" -ForegroundColor Cyan

# Ensure we are in the project root
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
Set-Location $root

Write-Host "Context: UniMap360PostgresContext" -ForegroundColor Yellow
dotnet ef database update --context UniMap360PostgresContext

if ($LASTEXITCODE -ne 0) {
    Write-Error "Migration failed! Please check if 'postgis' extension is enabled on your Supabase dashboard."
} else {
    Write-Host "Migration applied successfully!" -ForegroundColor Green
}
