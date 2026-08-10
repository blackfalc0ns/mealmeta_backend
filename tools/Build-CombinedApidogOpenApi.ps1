[CmdletBinding()]
param(
    [string]$InputPath = "docs/apidog/mealmate-openapi.json",
    [string]$OutputPath = "docs/apidog/mealmate-apidog.openapi.json"
)

$ErrorActionPreference = "Stop"
$spec = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
$prefixes = [ordered]@{
    Customer = @("/api/v1/auth/customers/", "/api/v1/customers", "/api/v1/customer_subscription_pricing", "/api/v1/family_subscription", "/api/v1/promo_referral_discount_codes", "/api/v1/influencers", "/api/v1/subscriptions")
    Driver = @("/api/v1/auth/drivers/", "/api/v1/drivers/registrations", "/api/v1/documents")
    Restaurant = @("/api/v1/auth/restaurants/", "/api/v1/restaurant/", "/api/v1/restaurants/", "/api/v1/restaurant_bundle_pricing", "/api/v1/restaurant_offerings", "/api/v1/bundles_management", "/api/v1/nutrition_programs_management", "/api/v1/dynamic_restaurant_classification", "/api/v1/subscription_pricing_settings", "/api/v1/uploads")
    "Delivery Manager" = @("/api/v1/auth/delivery-managers/")
    Admin = @("/api/v1/auth/admin/", "/api/v1/admin/", "/api/v1/audit-logs", "/api/v1/settings/", "/api/v1/subscription_lifecycle_operations")
    Shared = @("/api/v1/auth/refresh", "/api/v1/auth/logout", "/api/v1/catalog/", "/api/v1/webhooks/")
}

function Get-Platform([string]$path) {
    foreach ($platform in $prefixes.Keys) {
        foreach ($prefix in $prefixes[$platform]) {
            if ($path.Equals($prefix, [StringComparison]::OrdinalIgnoreCase) -or $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $platform }
        }
    }
    return "Shared"
}

$methods = @("get", "post", "put", "patch", "delete", "options", "head", "trace")
foreach ($path in $spec.paths.PSObject.Properties) {
    $platform = Get-Platform $path.Name
    foreach ($method in $methods) {
        $operation = $path.Value.PSObject.Properties[$method]
        if ($null -ne $operation) {
            $operation.Value.tags = @($platform)
        }
    }
}

$spec.info.title = "MealMate Unified API"
$spec.info.description = "Unified OpenAPI contract for all MealMate platforms. Operations are grouped by platform tags for Apidog."
$spec | Add-Member -MemberType NoteProperty -Name tags -Value @(
    [pscustomobject]@{ name = "Customer"; description = "Customer app: registration, OTP, login, profile and subscriptions." },
    [pscustomobject]@{ name = "Driver"; description = "Driver app: registration, OTP, documents and account access." },
    [pscustomobject]@{ name = "Restaurant"; description = "Restaurant app: onboarding, menus, meals, operations and pricing." },
    [pscustomobject]@{ name = "Delivery Manager"; description = "Delivery manager authentication and account operations." },
    [pscustomobject]@{ name = "Admin"; description = "Admin dashboard, approvals, RBAC, audit and settings." },
    [pscustomobject]@{ name = "Shared"; description = "Shared authentication, catalogs and webhooks." }
)

$parent = Split-Path -Parent $OutputPath
if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
$spec | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $OutputPath -Encoding utf8
Write-Host "Unified Apidog OpenAPI written to $OutputPath"
