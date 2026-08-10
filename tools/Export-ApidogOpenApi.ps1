[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5073",
    [string]$OutputPath = "docs/apidog/mealmate-openapi.json"
)

$ErrorActionPreference = "Stop"
$swaggerUrl = "$($BaseUrl.TrimEnd('/'))/swagger/v1/swagger.json"
$outputFile = Join-Path (Get-Location) $OutputPath

try {
    $spec = Invoke-RestMethod -Uri $swaggerUrl -Method Get
}
catch {
    throw "Could not download OpenAPI from $swaggerUrl. Start the API first, then run this script again. $($_.Exception.Message)"
}

# Make generated requests in Apidog target the same API instance used to export the file.
$servers = @([pscustomobject]@{ url = $BaseUrl.TrimEnd('/') })
if ($spec.PSObject.Properties.Name -contains "servers") {
    $spec.servers = $servers
}
else {
    $spec | Add-Member -MemberType NoteProperty -Name servers -Value $servers
}

$parent = Split-Path -Parent $outputFile
if ($parent -and -not (Test-Path -LiteralPath $parent)) {
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
}

$spec | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $outputFile -Encoding utf8
Write-Host "OpenAPI exported for Apidog: $outputFile"
Write-Host "Import this file in Apidog using Import -> OpenAPI, then set the environment base URL if needed."
