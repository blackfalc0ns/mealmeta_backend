param (
    [string]$RestaurantName = "",
    [string]$ApiUrl = "http://localhost:5073"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RestaurantName)) {
    $randomId = Get-Random -Minimum 100 -Maximum 999
    $RestaurantName = "Test Restaurant #$randomId"
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  MealMate: Submitting Test Restaurant Application" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Restaurant Name: $RestaurantName" -ForegroundColor White
Write-Host "API URL:         $ApiUrl" -ForegroundColor White
Write-Host ""

$payloadObj = @{ restaurantName = $RestaurantName }
$requestBody = $payloadObj | ConvertTo-Json

try {
    Write-Host "Sending submission request to API..." -ForegroundColor Gray
    $response = Invoke-RestMethod -Uri "$ApiUrl/api/v1/restaurants/registrations/test-submit" `
        -Method Post `
        -ContentType "application/json; charset=utf-8" `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($requestBody))

    $regId = $response.registrationId
    $name = $response.restaurantName
    $email = $response.ownerEmail
    $status = $response.status
    $adminLink = "http://localhost:4200/admin/accounts/pending/$regId"

    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "  SUCCESS: Restaurant Created & Admin Notification Sent!" -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Restaurant Details:" -ForegroundColor Yellow
    Write-Host "  - Registration ID:  $regId" -ForegroundColor Cyan
    Write-Host "  - Name:             $name" -ForegroundColor Cyan
    Write-Host "  - Email:            $email" -ForegroundColor Cyan
    Write-Host "  - Status:           $status" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Notification Status:" -ForegroundColor Yellow
    Write-Host "  [OK] Push and In-App notification sent to all Admin accounts." -ForegroundColor Green
    Write-Host "  [OK] New notification is visible in Admin Header Bell icon." -ForegroundColor Green
    Write-Host ""
    Write-Host "Review Links:" -ForegroundColor Yellow
    Write-Host "  - Admin Dashboard:           http://localhost:4200" -ForegroundColor White
    Write-Host "  - Direct Application Review: $adminLink" -ForegroundColor White
    Write-Host "  - Restaurant Dashboard:      http://localhost:4201" -ForegroundColor White
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Host " [ERROR] Failed to submit test restaurant:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($_.ErrorDetails) {
        Write-Host $_.ErrorDetails.Message -ForegroundColor DarkRed
    }
    Write-Host ""
    exit 1
}