param(
    [string]$BaseUrl = "http://localhost:5274",
    [string]$AdminEmail = "admin@unimap360.local",
    [string]$AdminPassword = "123456",
    [string]$SeedVersion = "v3",
    [int]$JobTargetPerProvince = 10,
    [int]$RoomTargetPerProvince = 20,
    [int]$RoomMaxPages = 10,
    [int]$EnrichLimit = 5000
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Invoke-JsonApi {
    param(
        [string]$Method,
        [string]$Url,
        [hashtable]$Headers,
        [object]$Body = $null
    )

    if ($null -ne $Body) {
        return Invoke-RestMethod -Method $Method -Uri $Url -Headers $Headers -ContentType "application/json" -Body ($Body | ConvertTo-Json -Depth 10)
    }

    return Invoke-RestMethod -Method $Method -Uri $Url -Headers $Headers
}

$provinceSlugs = @(
    "ha-noi","ho-chi-minh","da-nang","hai-phong","can-tho",
    "an-giang","ba-ria-vung-tau","bac-giang","bac-kan","bac-lieu","bac-ninh",
    "ben-tre","binh-dinh","binh-duong","binh-phuoc","binh-thuan","ca-mau","cao-bang",
    "dak-lak","dak-nong","dien-bien","dong-nai","dong-thap","gia-lai","ha-giang","ha-nam",
    "ha-tinh","hai-duong","hau-giang","hoa-binh","hung-yen","khanh-hoa","kien-giang","kon-tum",
    "lai-chau","lam-dong","lang-son","lao-cai","long-an","nam-dinh","nghe-an","ninh-binh","ninh-thuan",
    "phu-tho","phu-yen","quang-binh","quang-nam","quang-ngai","quang-ninh","quang-tri","soc-trang",
    "son-la","tay-ninh","thai-binh","thai-nguyen","thanh-hoa","thua-thien-hue","tien-giang","tra-vinh",
    "tuyen-quang","vinh-long","vinh-phuc","yen-bai"
)

Write-Step "Reset content tables in SQL Server"
$sql = @"
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;
SET NOCOUNT ON;
BEGIN TRY
  BEGIN TRAN;
  DELETE FROM dbo.JobApplications;
  DELETE FROM dbo.Favorites;
  DELETE FROM dbo.Reviews;
  DELETE FROM dbo.Media WHERE TargetType IN ('Room','Job');
  DELETE FROM dbo.Jobs;
  DELETE FROM dbo.Rooms;
  DELETE FROM dbo.Locations;
  COMMIT;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK;
  THROW;
END CATCH;
"@
sqlcmd -S "." -E -d "UniMap360_Pro" -C -b -Q $sql | Out-Null
Write-Host "Tables cleared." -ForegroundColor Green

Write-Step "Login admin to get JWT token"
$loginBody = @{
    email = $AdminEmail
    password = $AdminPassword
}
$loginRes = Invoke-JsonApi -Method "POST" -Url "$BaseUrl/api/auth/login" -Headers @{} -Body $loginBody
if (-not $loginRes.accessToken) {
    throw "Cannot get access token from $BaseUrl/api/auth/login"
}
$headers = @{
    Authorization = "Bearer $($loginRes.accessToken)"
}
Write-Host "Login success." -ForegroundColor Green

Write-Step "Seed Jobs for 63 provinces"
$i = 0
foreach ($slug in $provinceSlugs) {
    $i++
    $url = "$BaseUrl/api/seed/jobs/province?provinceSlug=$slug&target=$JobTargetPerProvince&seedVersion=$SeedVersion"
    $res = Invoke-JsonApi -Method "POST" -Url $url -Headers $headers
    Write-Host ("[{0}/{1}] jobs {2} inserted={3} skipped={4}" -f $i, $provinceSlugs.Count, $slug, $res.inserted, $res.skipped)
    Start-Sleep -Milliseconds 120
}

Write-Step "Seed Rooms for 63 provinces"
$i = 0
foreach ($slug in $provinceSlugs) {
    $i++
    $url = "$BaseUrl/api/scrape/rooms/phongtro123?provinceSlug=$slug&target=$RoomTargetPerProvince&maxPages=$RoomMaxPages"
    $res = Invoke-JsonApi -Method "POST" -Url $url -Headers $headers
    Write-Host ("[{0}/{1}] rooms {2} inserted={3} skipped={4} errors={5}" -f $i, $provinceSlugs.Count, $slug, $res.inserted, $res.skipped, $res.errors)
    Start-Sleep -Milliseconds 900
}

Write-Step "Enrich room images (3-4 images/room, replace all)"
$enrichUrl = "$BaseUrl/api/scrape/rooms/enrich-images?minImages=3&maxImages=4&limit=$EnrichLimit&replaceAll=true"
$enrichRes = Invoke-JsonApi -Method "POST" -Url $enrichUrl -Headers $headers
Write-Host ("enrich inserted={0} affectedRooms={1}" -f $enrichRes.inserted, $enrichRes.affectedRooms) -ForegroundColor Green

Write-Step "Final counts"
$countSql = @"
SET NOCOUNT ON;
SELECT 'Rooms' AS Tbl, COUNT(*) AS Cnt FROM dbo.Rooms
UNION ALL SELECT 'Jobs', COUNT(*) FROM dbo.Jobs
UNION ALL SELECT 'Media(Room)', COUNT(*) FROM dbo.Media WHERE TargetType='Room'
UNION ALL SELECT 'Media(Job)', COUNT(*) FROM dbo.Media WHERE TargetType='Job'
UNION ALL SELECT 'Locations', COUNT(*) FROM dbo.Locations;
"@
sqlcmd -S "." -E -d "UniMap360_Pro" -C -Q $countSql

Write-Host ""
Write-Host "DONE: reset + seed completed." -ForegroundColor Green
