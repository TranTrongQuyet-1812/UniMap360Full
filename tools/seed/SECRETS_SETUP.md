# Secrets Setup (Development)

Do not keep real secrets in `appsettings*.json`.

## Option A (recommended for this project): `secrets.ini`

The project now loads `secrets.ini` automatically at startup.

File: `secrets.ini` (project root)

```ini
[ConnectionStrings]
DefaultConnection=Server=.;Database=UniMap360_Pro;Trusted_Connection=True;TrustServerCertificate=True;

[Jwt]
Key=UniMap360_SuperSecretKey_ChangeThis_2026!
Issuer=UniMap360
Audience=UniMap360.Client
ExpiresMinutes=120

[Auth]
LegacyHashFallbackPlainPassword=123456

[Cloudinary]
Enabled=true
RequireSuccess=true
CloudinaryUrl=cloudinary://<api_key>:<api_secret>@<cloud_name>
CloudName=<cloud_name>
ApiKey=<api_key>
ApiSecret=<api_secret>
```

## Option B: environment variables (PowerShell)

```powershell
$env:Cloudinary__Enabled="true"
$env:Cloudinary__RequireSuccess="true"
$env:Cloudinary__CloudinaryUrl="cloudinary://<api_key>:<api_secret>@<cloud_name>"
$env:Cloudinary__CloudName="<cloud_name>"
$env:Cloudinary__ApiKey="<api_key>"
$env:Cloudinary__ApiSecret="<api_secret>"
```

Run `dotnet run` in the same terminal after setting these variables.

## 1-click reset + reseed

```powershell
powershell -ExecutionPolicy Bypass -File ".\tools\seed\reset-and-seed.ps1"
```

Optional params:

```powershell
powershell -ExecutionPolicy Bypass -File ".\tools\seed\reset-and-seed.ps1" `
  -BaseUrl "http://localhost:5274" `
  -AdminEmail "admin@unimap360.local" `
  -AdminPassword "123456" `
  -SeedVersion "v3" `
  -JobTargetPerProvince 10 `
  -RoomTargetPerProvince 20 `
  -RoomMaxPages 10 `
  -EnrichLimit 5000
```
