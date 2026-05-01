<#
.SYNOPSIS
    UniMap360 - Integration Test Suite (PowerShell)
.DESCRIPTION
    Bộ test tích hợp (Integration Test) cho các luồng API cốt lõi.
    Chạy bằng PowerShell, không cần cài thêm NuGet package.
    Yêu cầu: Server đang chạy ở http://localhost:5062 (hoặc sửa biến $BaseUrl).
.USAGE
    1. Chạy server: dotnet run
    2. Mở terminal mới, chạy: .\tests\Run-IntegrationTests.ps1
#>

param(
    [string]$BaseUrl = "http://localhost:5062"
)

$ErrorActionPreference = "Continue"
$totalTests = 0
$passedTests = 0
$failedTests = 0
$failedNames = @()

function Write-TestHeader {
    param([string]$SuiteName)
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  $SuiteName" -ForegroundColor Cyan
    Write-Host "============================================" -ForegroundColor Cyan
}

function Assert-Test {
    param(
        [string]$TestName,
        [bool]$Condition,
        [string]$FailMessage = ""
    )
    $script:totalTests++
    if ($Condition) {
        $script:passedTests++
        Write-Host "  [PASS] $TestName" -ForegroundColor Green
    } else {
        $script:failedTests++
        $script:failedNames += $TestName
        Write-Host "  [FAIL] $TestName" -ForegroundColor Red
        if ($FailMessage) { Write-Host "         -> $FailMessage" -ForegroundColor Yellow }
    }
}

function Invoke-Api {
    param(
        [string]$Method = "GET",
        [string]$Endpoint,
        [object]$Body = $null,
        [string]$Token = "",
        [string]$ContentType = "application/json"
    )
    $uri = "$BaseUrl$Endpoint"
    $headers = @{}
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }
    if ($ContentType) { $headers["Content-Type"] = $ContentType }

    try {
        $params = @{
            Uri = $uri
            Method = $Method
            Headers = $headers
            ErrorAction = "Stop"
        }
        if ($Body -and $Method -ne "GET") {
            $jsonBody = if ($Body -is [string]) { $Body } else { $Body | ConvertTo-Json -Depth 10 }
            $params["Body"] = $jsonBody
        }
        $response = Invoke-RestMethod @params
        return @{ Success = $true; Data = $response; StatusCode = 200 }
    } catch {
        $statusCode = 0
        $responseBody = $null
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
            try {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $responseBody = $reader.ReadToEnd() | ConvertFrom-Json
                $reader.Close()
            } catch {}
        }
        return @{ Success = $false; Data = $responseBody; StatusCode = $statusCode; Error = $_.Exception.Message }
    }
}

# ============================================================
# SUITE 0: SETUP — Tạo tài khoản test mới
# ============================================================
$timestamp = Get-Date -Format "yyyyMMddHHmmss"
$testEmail = "test_runner_$timestamp@test.com"
$testPassword = "TestPass$timestamp"

Write-Host ""
Write-Host "  [SETUP] Dang ky tai khoan test: $testEmail" -ForegroundColor DarkGray

$regResult = Invoke-Api -Method POST -Endpoint "/api/auth/register" -Body @{ email = $testEmail; password = $testPassword; role = "Student" }

# ============================================================
# SUITE 1: AUTH — Register / Login / Me / Refresh
# ============================================================
Write-TestHeader "SUITE 1: Auth (Register / Login / Me / Refresh)"

# Test 1.0: Register thành công
$regOk = $regResult.Success -and $regResult.Data.success -eq $true
Assert-Test "1.0 Register tai khoan test thanh cong" $regOk

# Test 1.1: Login với tài khoản vừa tạo
$loginResult = Invoke-Api -Method POST -Endpoint "/api/auth/login" -Body @{ email = $testEmail; password = $testPassword }
$loginOk = $loginResult.Success -and $loginResult.Data.success -eq $true -and $loginResult.Data.data.accessToken
Assert-Test "1.1 Login thanh cong voi tai khoan hop le" $loginOk

$accessToken = if ($loginOk) { $loginResult.Data.data.accessToken } else { "" }

# Test 1.2: Login với mật khẩu sai
$loginBad = Invoke-Api -Method POST -Endpoint "/api/auth/login" -Body @{ email = $testEmail; password = "wrong_password" }
Assert-Test "1.2 Login that bai voi mat khau sai (401)" ($loginBad.StatusCode -eq 401)

# Test 1.3: Login với email không tồn tại
$loginNoUser = Invoke-Api -Method POST -Endpoint "/api/auth/login" -Body @{ email = "notexist_$timestamp@xyz.com"; password = "123456" }
Assert-Test "1.3 Login that bai voi email khong ton tai (401)" ($loginNoUser.StatusCode -eq 401)

# Test 1.4: GET /api/auth/me với token hợp lệ
$meResult = Invoke-Api -Method GET -Endpoint "/api/auth/me" -Token $accessToken
$meOk = $meResult.Success -and $meResult.Data.success -eq $true -and $meResult.Data.data.email
Assert-Test "1.4 GET /me tra ve thong tin tai khoan" $meOk

# Test 1.5: GET /api/auth/me không có token
$meNoToken = Invoke-Api -Method GET -Endpoint "/api/auth/me"
Assert-Test "1.5 GET /me khong co token tra ve 401" ($meNoToken.StatusCode -eq 401)

# Test 1.6: POST /api/auth/refresh với token hợp lệ
$refreshResult = Invoke-Api -Method POST -Endpoint "/api/auth/refresh" -Token $accessToken
$refreshOk = $refreshResult.Success -and $refreshResult.Data.success -eq $true -and $refreshResult.Data.data.accessToken
Assert-Test "1.6 Refresh token thanh cong" $refreshOk

# ============================================================
# SUITE 2: LISTINGS — Room/Job listing, filter, pagination
# ============================================================
Write-TestHeader "SUITE 2: Listings (Room/Job Feed)"

# Test 2.1: GET /api/listings/cards (tất cả — pageSize lớn hơn để lấy cả room và job)
$listAll = Invoke-Api -Method GET -Endpoint "/api/listings/cards?page=1&pageSize=20"
$listAllOk = $listAll.Success -and $listAll.Data.success -eq $true -and $null -ne $listAll.Data.data.items
Assert-Test "2.1 GET /listings/cards tra ve danh sach" $listAllOk

# Test 2.2: Filter theo type=room
$listRoom = Invoke-Api -Method GET -Endpoint "/api/listings/cards?type=room&pageSize=3"
$listRoomOk = $listRoom.Success -and $listRoom.Data.success -eq $true
Assert-Test "2.2 Filter type=room" $listRoomOk

# Test 2.3: Filter theo type=job
$listJob = Invoke-Api -Method GET -Endpoint "/api/listings/cards?type=job&pageSize=3"
$listJobOk = $listJob.Success -and $listJob.Data.success -eq $true
Assert-Test "2.3 Filter type=job" $listJobOk

# Test 2.4: Pagination — page 2
$listPage2 = Invoke-Api -Method GET -Endpoint "/api/listings/cards?page=2&pageSize=2"
$listPage2Ok = $listPage2.Success -and $listPage2.Data.success -eq $true -and $listPage2.Data.data.page -eq 2
Assert-Test "2.4 Pagination page=2 tra ve dung page" $listPage2Ok

# ============================================================
# SUITE 3: DETAILS — Room/Job detail
# ============================================================
Write-TestHeader "SUITE 3: Details (Room/Job chi tiet)"

# Lấy ID đầu tiên từ danh sách listings nếu có
$firstRoomId = $null
$firstJobId = $null
if ($listAllOk -and $listAll.Data.data.items.Count -gt 0) {
    foreach ($item in $listAll.Data.data.items) {
        if ($item.type -eq "room" -and -not $firstRoomId) { $firstRoomId = $item.id }
        if ($item.type -eq "job" -and -not $firstJobId) { $firstJobId = $item.id }
    }
}

# Test 3.1: Room detail
if ($firstRoomId) {
    $roomDetail = Invoke-Api -Method GET -Endpoint "/api/rooms/$firstRoomId"
    $roomDetailOk = $roomDetail.Success -and $roomDetail.Data.success -eq $true -and $roomDetail.Data.data.roomId -eq $firstRoomId
    Assert-Test "3.1 GET /rooms/$firstRoomId tra ve chi tiet phong" $roomDetailOk
} else {
    Assert-Test "3.1 GET /rooms/{id} (SKIP - khong co room trong DB)" $false "Khong tim thay Room ID trong listings"
}

# Test 3.2: Job detail
if ($firstJobId) {
    $jobDetail = Invoke-Api -Method GET -Endpoint "/api/jobs/$firstJobId"
    $jobDetailOk = $jobDetail.Success -and $jobDetail.Data.success -eq $true -and $jobDetail.Data.data.jobId -eq $firstJobId
    Assert-Test "3.2 GET /jobs/$firstJobId tra ve chi tiet viec lam" $jobDetailOk
} else {
    Assert-Test "3.2 GET /jobs/{id} (SKIP - khong co job trong DB)" $false "Khong tim thay Job ID trong listings"
}

# Test 3.3: Room not found
$room404 = Invoke-Api -Method GET -Endpoint "/api/rooms/999999"
Assert-Test "3.3 GET /rooms/999999 tra ve 404" ($room404.StatusCode -eq 404)

# Test 3.4: Job not found
$job404 = Invoke-Api -Method GET -Endpoint "/api/jobs/999999"
Assert-Test "3.4 GET /jobs/999999 tra ve 404" ($job404.StatusCode -eq 404)

# ============================================================
# SUITE 4: NOTIFICATIONS — CRUD
# ============================================================
Write-TestHeader "SUITE 4: Notifications"

# Test 4.1: GET /api/notifications (có token)
$notiList = Invoke-Api -Method GET -Endpoint "/api/notifications" -Token $accessToken
$notiListOk = $notiList.Success -and $notiList.Data.success -eq $true
Assert-Test "4.1 GET /notifications tra ve danh sach" $notiListOk

# Test 4.2: GET /api/notifications/summary
$notiSummary = Invoke-Api -Method GET -Endpoint "/api/notifications/summary" -Token $accessToken
$notiSummaryOk = $notiSummary.Success -and $notiSummary.Data.success -eq $true -and $null -ne $notiSummary.Data.data.unreadCount
Assert-Test "4.2 GET /notifications/summary tra ve unreadCount" $notiSummaryOk

# Test 4.3: GET /api/notifications không có token -> 401
$notiNoAuth = Invoke-Api -Method GET -Endpoint "/api/notifications"
Assert-Test "4.3 GET /notifications khong co token tra ve 401" ($notiNoAuth.StatusCode -eq 401)

# ============================================================
# SUITE 5: MAP FEED — /api/feed
# ============================================================
Write-TestHeader "SUITE 5: Map Feed"

$feedResult = Invoke-Api -Method GET -Endpoint "/api/feed"
$feedOk = $feedResult.Success -and $feedResult.Data.success -eq $true
Assert-Test "5.1 GET /api/feed tra ve du lieu ban do" $feedOk

# ============================================================
# SUITE 6: API CONTRACT — Envelope format
# ============================================================
Write-TestHeader "SUITE 6: API Contract Validation"

# Test 6.1: Response có đúng envelope format (success, data, error)
if ($listAllOk) {
    $hasSuccess = $null -ne $listAll.Data.success
    $hasData = $listAll.Data.PSObject.Properties.Name -contains "data"
    $hasError = $listAll.Data.PSObject.Properties.Name -contains "error"
    Assert-Test "6.1 Response co dung envelope {success, data, error}" ($hasSuccess -and $hasData -and $hasError)
} else {
    Assert-Test "6.1 Response co dung envelope (SKIP)" $false
}

# Test 6.2: Error response có error.code và error.message
if ($loginBad.Data) {
    $hasErrorCode = $loginBad.Data.error -and $loginBad.Data.error.code
    $hasErrorMsg = $loginBad.Data.error -and $loginBad.Data.error.message
    Assert-Test "6.2 Error response co error.code va error.message" ($hasErrorCode -and $hasErrorMsg)
} else {
    Assert-Test "6.2 Error response co error.code va error.message (SKIP)" $false
}

# ============================================================
# SUITE 7: VALIDATION — DTO validation
# ============================================================
Write-TestHeader "SUITE 7: DTO Validation"

# Test 7.1: Register thiếu email
$regBad = Invoke-Api -Method POST -Endpoint "/api/auth/register" -Body @{ email = ""; password = "123456"; role = "Student" }
Assert-Test "7.1 Register thieu email tra ve loi validation" ($regBad.StatusCode -eq 400 -or ($regBad.Data -and $regBad.Data.success -eq $false))

# Test 7.2: Login body rỗng
$loginEmpty = Invoke-Api -Method POST -Endpoint "/api/auth/login" -Body @{ email = ""; password = "" }
Assert-Test "7.2 Login body rong tra ve loi" ($loginEmpty.StatusCode -eq 400 -or ($loginEmpty.Data -and $loginEmpty.Data.success -eq $false))

# ============================================================
# KẾT QUẢ TỔNG HỢP
# ============================================================
Write-Host ""
Write-Host "============================================" -ForegroundColor White
Write-Host "  KET QUA TONG HOP" -ForegroundColor White
Write-Host "============================================" -ForegroundColor White
Write-Host "  Tong so test : $totalTests" -ForegroundColor White
Write-Host "  PASS         : $passedTests" -ForegroundColor Green
Write-Host "  FAIL         : $failedTests" -ForegroundColor $(if ($failedTests -gt 0) { "Red" } else { "Green" })

if ($failedTests -gt 0) {
    Write-Host ""
    Write-Host "  Cac test FAIL:" -ForegroundColor Red
    foreach ($name in $failedNames) {
        Write-Host "    - $name" -ForegroundColor Red
    }
}

Write-Host "============================================" -ForegroundColor White
Write-Host ""

# Exit code cho CI/CD
if ($failedTests -gt 0) { exit 1 } else { exit 0 }
