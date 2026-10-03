[CmdletBinding()]
param(
    [string]$OutputDirectory = "artifacts/demo",
    [string]$CommitSha = $env:GITHUB_SHA
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$previousLocation = Get-Location
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("expensehub-verification-" + [Guid]::NewGuid().ToString("N"))
$apiProcess = $null
$previousEnvironment = @{}
$phase = "prepare"
$demoStarted = $false
$failed = $false
if ([string]::IsNullOrWhiteSpace($CommitSha)) { $CommitSha = "working-tree" }

function Invoke-Dotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw [InvalidOperationException]::new("The $phase command failed.") }
}

function Set-VerificationEnvironment {
    param([string]$Name, [string]$Value)
    $previousEnvironment[$Name] = [Environment]::GetEnvironmentVariable($Name, "Process")
    [Environment]::SetEnvironmentVariable($Name, $Value, "Process")
}

try {
    Set-Location $repositoryRoot
    $OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
    $null = New-Item -ItemType Directory -Path $temporaryDirectory, $OutputDirectory -Force
    foreach ($reportName in @("report.json", "report.md")) {
        $reportPath = Join-Path $OutputDirectory $reportName
        if (Test-Path $reportPath) { Remove-Item $reportPath -Force }
    }
    $phase = "tool restore"
    Invoke-Dotnet -Arguments @("tool", "restore")
    $phase = "restore"
    Invoke-Dotnet -Arguments @("restore", "sources/ExpenseHub.slnx")
    $phase = "build"
    Invoke-Dotnet -Arguments @("build", "sources/ExpenseHub.slnx", "--no-restore")
    $phase = "tests"
    Invoke-Dotnet -Arguments @("test", "sources/ExpenseHub.slnx", "--no-build", "--no-restore", "--logger", "trx", "--results-directory", (Join-Path $temporaryDirectory "test-results"))

    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        $port = ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally { $listener.Stop() }
    $baseUrl = "http://127.0.0.1:$port"
    $credential = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)) + "aA1!"
    Set-VerificationEnvironment "ASPNETCORE_URLS" $baseUrl
    Set-VerificationEnvironment "ASPNETCORE_ENVIRONMENT" "Development"
    Set-VerificationEnvironment "ConnectionStrings__ExpenseHub" ("Data Source=" + (Join-Path $temporaryDirectory "demo.db") + ";Pooling=False")
    Set-VerificationEnvironment "Seed__AdminEmail" "admin@example.test"
    Set-VerificationEnvironment "Seed__AdminPassword" $credential

    $phase = "migration"
    Invoke-Dotnet -Arguments @("ef", "database", "update", "--project", "sources/ExpenseHub.Api", "--startup-project", "sources/ExpenseHub.Api", "--no-build")
    $phase = "host startup"
    $apiDirectory = Join-Path $repositoryRoot "sources/ExpenseHub.Api"
    $apiDll = Join-Path $apiDirectory "bin/Debug/net10.0/ExpenseHub.Api.dll"
    $apiProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList ('"' + $apiDll + '"') -WorkingDirectory $apiDirectory -RedirectStandardOutput (Join-Path $temporaryDirectory "host.log") -RedirectStandardError (Join-Path $temporaryDirectory "host-error.log") -PassThru
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($apiProcess.HasExited) { throw [InvalidOperationException]::new("The temporary API exited during startup.") }
        try {
            $health = Invoke-RestMethod -Uri "$baseUrl/health" -TimeoutSec 2
            if ($health.status -eq "ok") { $ready = $true; break }
        }
        catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw [TimeoutException]::new("The temporary API did not become ready.") }

    $phase = "HTTP demo"
    $demoStarted = $true
    $shellName = if ($IsWindows) { "pwsh.exe" } else { "pwsh" }
    & (Join-Path $PSHOME $shellName) -NoProfile -File (Join-Path $PSScriptRoot "Invoke-ExpenseHubDemo.ps1") -BaseUrl $baseUrl -OutputDirectory $OutputDirectory -CommitSha $CommitSha
    if ($LASTEXITCODE -ne 0) { throw [InvalidOperationException]::new("The HTTP demo failed. Read its report.") }
}
catch {
    $failed = $true
    if (-not $demoStarted) {
        $null = New-Item -ItemType Directory -Path $OutputDirectory -Force
        $report = [ordered]@{
            commit = $CommitSha
            status = "failed"
            phase = $phase
            finishedAtUtc = [DateTime]::UtcNow.ToString("O", [Globalization.CultureInfo]::InvariantCulture)
            summary = @{ total = 1; passed = 0; failed = 1 }
            scenarios = @(@{ name = "Verification bootstrap: $phase"; passed = $false; expectedStatus = $null; observedStatus = $null; traceId = $null })
        }
        $report | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $OutputDirectory "report.json") -Encoding utf8
        "# ExpenseHub verification`n`nCommit: $CommitSha`n`nStatus: failed`n`nBootstrap phase: $phase`n`nNo HTTP scenario was completed." | Set-Content (Join-Path $OutputDirectory "report.md") -Encoding utf8
    }
    Write-Warning "Verification failed during $phase. Read $OutputDirectory/report.json."
}
finally {
    if ($null -ne $apiProcess) {
        if (-not $apiProcess.HasExited) { $apiProcess.Kill(); $apiProcess.WaitForExit() }
        $apiProcess.Dispose()
    }
    foreach ($entry in $previousEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
    }
    if (Test-Path $temporaryDirectory) { Remove-Item $temporaryDirectory -Recurse -Force }
    Set-Location $previousLocation
}

if ($failed) { exit 1 }
Write-Host "Build, tests, migration and HTTP demo passed."
