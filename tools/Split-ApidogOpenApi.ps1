[CmdletBinding()]
param(
    [string]$InputPath = "docs/apidog/mealmate-openapi.json",
    [string]$OutputDirectory = "docs/apidog/platforms"
)

$ErrorActionPreference = "Stop"
$source = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
$outputRoot = Join-Path (Get-Location) $OutputDirectory
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$groups = [ordered]@{
    customer = @(
        "/api/v1/auth/customers/", "/api/v1/customers", "/api/v1/customer_subscription_pricing",
        "/api/v1/family_subscription", "/api/v1/promo_referral_discount_codes", "/api/v1/influencers",
        "/api/v1/subscriptions"
    )
    driver = @(
        "/api/v1/auth/drivers/", "/api/v1/drivers/registrations", "/api/v1/documents"
    )
    restaurant = @(
        "/api/v1/auth/restaurants/", "/api/v1/restaurant/", "/api/v1/restaurants/",
        "/api/v1/restaurant_bundle_pricing", "/api/v1/restaurant_offerings", "/api/v1/bundles_management",
        "/api/v1/nutrition_programs_management", "/api/v1/dynamic_restaurant_classification",
        "/api/v1/subscription_pricing_settings", "/api/v1/uploads"
    )
    delivery_manager = @("/api/v1/auth/delivery-managers/")
    admin = @(
        "/api/v1/auth/admin/", "/api/v1/admin/", "/api/v1/audit-logs",
        "/api/v1/settings/", "/api/v1/subscription_lifecycle_operations"
    )
    common = @("/api/v1/auth/refresh", "/api/v1/auth/logout", "/api/v1/catalog/", "/api/v1/webhooks/")
}

function Test-Prefix([string]$path, [string[]]$prefixes) {
    foreach ($prefix in $prefixes) {
        if ($path.Equals($prefix, [StringComparison]::OrdinalIgnoreCase) -or
            $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

$pathsByGroup = @{}
foreach ($name in $groups.Keys) { $pathsByGroup[$name] = [ordered]@{} }
$unassigned = @()
foreach ($entry in $source.paths.PSObject.Properties) {
    $owner = $null
    foreach ($name in $groups.Keys) {
        if (Test-Prefix $entry.Name $groups[$name]) { $owner = $name; break }
    }
    if ($null -eq $owner) { $unassigned += $entry.Name } else { $pathsByGroup[$owner][$entry.Name] = $entry.Value }
}
if ($unassigned.Count -gt 0) { $unassigned | ForEach-Object { $pathsByGroup.common[$_] = $source.paths.$_ } }

$titles = @{
    customer = "MealMate Customer API"; driver = "MealMate Driver API"; restaurant = "MealMate Restaurant API"
    delivery_manager = "MealMate Delivery Manager API"; admin = "MealMate Admin API"; common = "MealMate Shared API"
}

foreach ($name in $groups.Keys) {
    $document = [ordered]@{
        openapi = $source.openapi
        info = [ordered]@{ title = $titles[$name]; version = $source.info.version; description = "MealMate $name platform endpoints. Import this file as a separate Apidog project." }
        servers = $source.servers
        tags = @([ordered]@{ name = $name; description = "$($titles[$name]) endpoints" })
        paths = $pathsByGroup[$name]
        components = $source.components
    }
    $target = Join-Path $outputRoot "$name.openapi.json"
    $document | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $target -Encoding utf8
    Write-Host "Exported $name ($($pathsByGroup[$name].Count) paths): $target"
}

$readme = @"
# MealMate Apidog projects

Import each OpenAPI file as a separate Apidog project:

| Platform | File |
|---|---|
| Customer | `customer.openapi.json` |
| Driver | `driver.openapi.json` |
| Restaurant | `restaurant.openapi.json` |
| Delivery Manager | `delivery_manager.openapi.json` |
| Admin | `admin.openapi.json` |
| Shared | `common.openapi.json` |

The legacy customer registration route `/api/v1/auth/register/customer` is intentionally absent.
Use `/api/v1/auth/customers/register` only.
"@
$readme | Set-Content -LiteralPath (Join-Path $outputRoot "README.md") -Encoding utf8
