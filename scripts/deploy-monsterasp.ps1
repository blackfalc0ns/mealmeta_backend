param(
    [Parameter(Mandatory = $true)]
    [string] $PublishPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function First-Value {
    param([string[]] $Values)
    foreach ($value in $Values) {
        if (-not [string]::IsNullOrWhiteSpace($value)) { return $value.Trim() }
    }
    return ''
}

$publishDirectory = (Resolve-Path -LiteralPath $PublishPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'MealMate.Api.dll'))) {
    throw 'Published API assembly is missing.'
}
$server = First-Value @($env:FTP_SERVER, $env:SERVER_COMPUTER_NAME)
if (-not $server -and $env:WEBSITE_NAME) { $server = "$($env:WEBSITE_NAME).siteasp.net" }
if (-not $server) { throw 'Set FTP_SERVER or SERVER_COMPUTER_NAME.' }
if ($server -notmatch '^[a-z]+://') { $server = "sftp://$server" }
$serverUri = [Uri] $server
$username = First-Value @($env:FTP_USERNAME, $env:SERVER_USERNAME, $env:WEBSITE_NAME)
$password = $env:FTP_PASSWORD
if ([string]::IsNullOrEmpty($password)) { $password = $env:SERVER_PASSWORD }
if ($password) { $password = $password.TrimEnd("`r", "`n") }
if (-not $username -or -not $password) { throw 'Missing SFTP username/password secrets.' }
$remoteDirectory = First-Value @($env:FTP_SERVER_DIR, '/wwwroot/')
$remoteDirectory = '/' + $remoteDirectory.Trim('/')
$remoteDirectory += '/'

# Pin the client version and use its .NET Standard assembly with PowerShell 7.
$toolDirectory = Join-Path $env:RUNNER_TEMP 'winscp-6.5.7'
New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
$packagePath = Join-Path $toolDirectory 'winscp.zip'
Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/winscp/6.5.7/winscp.6.5.7.nupkg' -OutFile $packagePath
Expand-Archive -LiteralPath $packagePath -DestinationPath $toolDirectory -Force
$assembly = Get-ChildItem -LiteralPath $toolDirectory -Filter WinSCPnet.dll -Recurse |
    Where-Object { $_.DirectoryName -match 'netstandard2.0' } | Select-Object -First 1
$executable = Get-ChildItem -LiteralPath $toolDirectory -Filter WinSCP.exe -Recurse | Select-Object -First 1
if (-not $assembly -or -not $executable) { throw 'WinSCP package is incomplete.' }
Add-Type -Path $assembly.FullName

$sessionOptions = New-Object WinSCP.SessionOptions
$sessionOptions.Protocol = [WinSCP.Protocol]::Sftp
$sessionOptions.HostName = $serverUri.Host
$sessionOptions.PortNumber = if ($env:FTP_SERVER -and $serverUri.Port -gt 0 -and -not $serverUri.IsDefaultPort) { $serverUri.Port } else { 22 }
$sessionOptions.UserName = $username
$sessionOptions.Password = $password
$sessionOptions.Timeout = [TimeSpan]::FromSeconds(30)
if ($env:FTP_HOST_KEY) {
    $sessionOptions.SshHostKeyFingerprint = $env:FTP_HOST_KEY.Trim()
} else {
    # Preserve first-connection behavior of the previous SFTP workflow.
    $sessionOptions.SshHostKeyPolicy = [WinSCP.SshHostKeyPolicy]::AcceptNew
}

$session = New-Object WinSCP.Session
$session.ExecutablePath = $executable.FullName
$session.ReconnectTime = [TimeSpan]::FromSeconds(30)
try {
    $session.Open($sessionOptions)
    if (-not $session.FileExists($remoteDirectory)) { throw 'SFTP destination directory does not exist.' }
    $offlineFile = Join-Path $env:RUNNER_TEMP 'app_offline.htm'
    [IO.File]::WriteAllBytes($offlineFile, [byte[]] @())
    $session.PutFiles($offlineFile, "$($remoteDirectory)app_offline.htm").Check()
    Start-Sleep -Seconds 15

    $options = New-Object WinSCP.TransferOptions
    $options.TransferMode = [WinSCP.TransferMode]::Binary
    $options.PreserveTimestamp = $true
    $options.FilePermissions = $null
    $options.FileMask = '| *.pdb; app_offline.htm; *.old; logs/'
    $uploaded = $false
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            # Never delete remote-only application data or hosting settings.
            $result = $session.SynchronizeDirectories(
                [WinSCP.SynchronizationMode]::Remote,
                $publishDirectory, $remoteDirectory, $false, $false,
                [WinSCP.SynchronizationCriteria]::Time, $options)
            $result.Check()
            $uploaded = $true
            break
        } catch {
            if ($attempt -eq 3) { throw }
            Write-Warning "SFTP upload attempt $attempt failed. Retrying after IIS releases file locks."
            Start-Sleep -Seconds 15
        }
    }
    if (-not $uploaded) { throw 'SFTP upload did not complete.' }
    # Check removal: never report successful deployment while IIS is offline.
    $session.RemoveFiles("$($remoteDirectory)app_offline.htm").Check()
    Write-Host 'API files uploaded successfully and app_offline.htm removed.'
} catch {
    Write-Error "Deployment failed. If app_offline.htm was uploaded, it remains in place to prevent serving a partial release. $($_.Exception.Message)"
    throw
} finally {
    $session.Dispose()
}
