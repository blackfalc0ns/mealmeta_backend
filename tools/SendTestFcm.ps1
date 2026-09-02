param(
    [Parameter(Mandatory = $true)]
    [string]$DeviceToken,

    [string]$Title = "اختبار MealMate",
    [string]$Body = "الإشعار من الـ backend وصل بنجاح ✅",
    [string]$CredentialPath = "$PSScriptRoot\..\secrets\firebase-service-account.json",
    [string]$ProjectId = "mealmate-bd5b3"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $CredentialPath)) {
    Write-Error "Missing Firebase service account JSON at: $CredentialPath"
}

function Get-FirebaseAccessToken {
    param([string]$JsonPath)

    $json = Get-Content $JsonPath -Raw | ConvertFrom-Json
    $now = [DateTimeOffset]::UtcNow
    $exp = $now.AddMinutes(55)

    $header = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('{"alg":"RS256","typ":"JWT"}')) -replace '\+','-' -replace '/','_' -replace '=+$',''
    $claim = @{
        iss = $json.client_email
        scope = "https://www.googleapis.com/auth/firebase.messaging"
        aud = "https://oauth2.googleapis.com/token"
        iat = $now.ToUnixTimeSeconds()
        exp = $exp.ToUnixTimeSeconds()
    } | ConvertTo-Json -Compress
    $claimB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($claim)) -replace '\+','-' -replace '/','_' -replace '=+$',''
    $unsigned = "$header.$claimB64"

    $rsa = [System.Security.Cryptography.RSA]::Create()
    $pem = "-----BEGIN PRIVATE KEY-----`n$($json.private_key -replace '-----BEGIN PRIVATE KEY-----','' -replace '-----END PRIVATE KEY-----','' -replace '\\n',"`n")`n-----END PRIVATE KEY-----"
    $rsa.ImportFromPem($pem)
    $sig = $rsa.SignData([Text.Encoding]::UTF8.GetBytes($unsigned), [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $sigB64 = [Convert]::ToBase64String($sig) -replace '\+','-' -replace '/','_' -replace '=+$',''
    $jwt = "$unsigned.$sigB64"

    $tokenResponse = Invoke-RestMethod -Method Post -Uri "https://oauth2.googleapis.com/token" -ContentType "application/x-www-form-urlencoded" -Body @{
        grant_type = "urn:ietf:params:oauth:grant-type:jwt-bearer"
        assertion  = $jwt
    }
    return $tokenResponse.access_token
}

$accessToken = Get-FirebaseAccessToken -JsonPath $CredentialPath

$payload = @{
    message = @{
        token = $DeviceToken
        notification = @{
            title = $Title
            body  = $Body
        }
        data = @{
            eventKey  = "push.test"
            subjectId = [guid]::Empty.ToString()
            deepLink  = "mealmate://home"
        }
        android = @{ priority = "HIGH" }
        apns = @{ payload = @{ aps = @{ sound = "default" } } }
    }
} | ConvertTo-Json -Depth 6

$response = Invoke-RestMethod -Method Post `
    -Uri "https://fcm.googleapis.com/v1/projects/$ProjectId/messages:send" `
    -Headers @{ Authorization = "Bearer $accessToken" } `
    -ContentType "application/json; charset=utf-8" `
    -Body $payload

Write-Host "Sent successfully." -ForegroundColor Green
Write-Host ($response | ConvertTo-Json -Compress)
