<#
.SYNOPSIS
    UniMap360 - Frontend Smoke Test Suite (PowerShell)
.DESCRIPTION
    Bộ smoke test cho các trang frontend chính.
    Kiểm tra xem server trả HTML thành công (status 200) và có đúng nội dung cần thiết hay không.
.USAGE
    1. Chạy server: dotnet run
    2. Mở terminal mới, chạy: .\tests\Run-SmokeTests.ps1
#>

param(
    [string]$BaseUrl = "http://localhost:5062"
)

$ErrorActionPreference = "Continue"
$totalTests = 0
$passedTests = 0
$failedTests = 0
$failedNames = @()

function Assert-Smoke {
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

function Test-Page {
    param(
        [string]$Path,
        [string]$TestName,
        [string[]]$MustContain = @()
    )
    $uri = "$BaseUrl$Path"
    try {
        $response = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
        $statusOk = $response.StatusCode -eq 200
        $contentOk = $true
        $missingContent = ""

        foreach ($keyword in $MustContain) {
            if ($response.Content -notmatch [regex]::Escape($keyword)) {
                $contentOk = $false
                $missingContent = "Thieu noi dung: '$keyword'"
                break
            }
        }

        Assert-Smoke $TestName ($statusOk -and $contentOk) $missingContent
    } catch {
        Assert-Smoke $TestName $false "HTTP Error: $($_.Exception.Message)"
    }
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  FRONTEND SMOKE TEST SUITE" -ForegroundColor Cyan
Write-Host "  Target: $BaseUrl" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# ============================================================
# 1. Trang Chủ (Map Page)
# ============================================================
Write-Host ""
Write-Host "--- 1. Trang Ban Do (Home/Index) ---" -ForegroundColor White
Test-Page -Path "/" -TestName "1.1 Trang chu load thanh cong (HTTP 200)" -MustContain @("UniMap360")
Test-Page -Path "/" -TestName "1.2 Trang chu co load map.js" -MustContain @("map.js")

# ============================================================
# 2. Trang Danh Sách (Listing)
# ============================================================
Write-Host ""
Write-Host "--- 2. Trang Danh Sach ---" -ForegroundColor White
Test-Page -Path "/Home/Listing" -TestName "2.1 Trang Listing load thanh cong" -MustContain @("listing")

# ============================================================
# 3. Trang Chi Tiết (Detail)
# ============================================================
Write-Host ""
Write-Host "--- 3. Trang Chi Tiet ---" -ForegroundColor White
Test-Page -Path "/Home/Detail?type=room&id=1" -TestName "3.1 Trang Detail Room load thanh cong"
Test-Page -Path "/Home/Detail?type=job&id=1" -TestName "3.2 Trang Detail Job load thanh cong"

# ============================================================
# 4. Trang Đăng Nhập/Đăng Ký (Auth)
# ============================================================
Write-Host ""
Write-Host "--- 4. Trang Auth ---" -ForegroundColor White
Test-Page -Path "/Home/Auth" -TestName "4.1 Trang Auth load thanh cong" -MustContain @("login", "password")

# ============================================================
# 5. Static Assets
# ============================================================
Write-Host ""
Write-Host "--- 5. Static Assets ---" -ForegroundColor White

function Test-Asset {
    param([string]$Path, [string]$TestName)
    $uri = "$BaseUrl$Path"
    try {
        $response = Invoke-WebRequest -Uri $uri -UseBasicParsing -Method Head -TimeoutSec 10 -ErrorAction Stop
        Assert-Smoke $TestName ($response.StatusCode -eq 200)
    } catch {
        Assert-Smoke $TestName $false "HTTP Error: $($_.Exception.Message)"
    }
}

Test-Asset -Path "/css/site.css" -TestName "5.1 site.css accessible"
Test-Asset -Path "/js/site.js" -TestName "5.2 site.js accessible"
Test-Asset -Path "/js/map.js" -TestName "5.3 map.js accessible"
Test-Asset -Path "/js/listing.js" -TestName "5.4 listing.js accessible"
Test-Asset -Path "/js/notification-center.js" -TestName "5.5 notification-center.js accessible"

# ============================================================
# 6. API Feed JSON (Smoke — kiểm tra server trả JSON đúng)
# ============================================================
Write-Host ""
Write-Host "--- 6. API Smoke ---" -ForegroundColor White

try {
    $feedResponse = Invoke-RestMethod -Uri "$BaseUrl/api/feed" -Method GET -TimeoutSec 10 -ErrorAction Stop
    $feedSmoke = $feedResponse.success -eq $true
    Assert-Smoke "6.1 /api/feed tra ve JSON hop le" $feedSmoke
} catch {
    Assert-Smoke "6.1 /api/feed tra ve JSON hop le" $false "Error: $($_.Exception.Message)"
}

try {
    $listResponse = Invoke-RestMethod -Uri "$BaseUrl/api/listings/cards?pageSize=1" -Method GET -TimeoutSec 10 -ErrorAction Stop
    $listSmoke = $listResponse.success -eq $true
    Assert-Smoke "6.2 /api/listings/cards tra ve JSON hop le" $listSmoke
} catch {
    Assert-Smoke "6.2 /api/listings/cards tra ve JSON hop le" $false "Error: $($_.Exception.Message)"
}

# ============================================================
# KẾT QUẢ TỔNG HỢP
# ============================================================
Write-Host ""
Write-Host "============================================" -ForegroundColor White
Write-Host "  KET QUA SMOKE TEST" -ForegroundColor White
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

if ($failedTests -gt 0) { exit 1 } else { exit 0 }
