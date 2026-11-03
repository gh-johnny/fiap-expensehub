[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:Passed = 0
$script:Failed = 0
$script:Workspace = Join-Path ([System.IO.Path]::GetTempPath()) "fiap-code-quality-e2e-$([guid]::NewGuid())"
$repositoryRoot = Split-Path $PSScriptRoot
$script:QualityScript = (Resolve-Path (Join-Path (Join-Path $repositoryRoot "scripts") "Invoke-CodeQuality.ps1")).Path
$script:SchemaPath = (Resolve-Path (Join-Path (Join-Path $repositoryRoot "docs") "code-quality-report.schema.json")).Path

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        [object]$Expected,
        [Parameter(Mandatory)]
        [object]$Actual,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if ("$Expected" -ne "$Actual") {
        throw "$Message. Esperado: <$Expected>. Obtido: <$Actual>."
    }
}

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool]$Condition,
        [Parameter(Mandatory)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Test-Case {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [scriptblock]$Body
    )

    try {
        & $Body
        $script:Passed++
        Write-Host "PASS $Name"
    }
    catch {
        $script:Failed++
        Write-Host "FAIL $Name"
        Write-Host "  $($_.Exception.Message)"
    }
}

function New-FakeTools {
    $tools = Join-Path $script:Workspace "tools"
    New-Item -ItemType Directory -Path $tools -Force | Out-Null

    @'
[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

if ($Arguments.Count -eq 1 -and $Arguments[0] -eq "--version") {
    Write-Output "10.0.100-test"
    exit 0
}

$operation = $Arguments[0]
$project = @($Arguments | Where-Object { $_ -like "*.csproj" } | Select-Object -First 1)
$projectContent = if ($project -and (Test-Path $project)) { Get-Content -Raw $project } else { "" }

switch ($operation) {
    "restore" { exit 0 }
    "build" {
        if ($projectContent -match "<BuildFailure>true</BuildFailure>") {
            $source = Join-Path (Split-Path $project) "Program.cs"
            Write-Output "$source(1,1): error CS1002: ; expected [$project]"
            exit 1
        }
        exit 0
    }
    "format" {
        $reportIndex = [Array]::IndexOf($Arguments, "--report")
        if ($reportIndex -ge 0) {
            Set-Content -Path $Arguments[$reportIndex + 1] -Value "[]"
        }
        exit 0
    }
    "test" {
        if ($projectContent -match "<FailTests>true</FailTests>") {
            Write-Output "Testes falharam intencionalmente."
            exit 1
        }
        exit 0
    }
    default { exit 0 }
}
'@ | Set-Content -Path (Join-Path $tools "dotnet.ps1")

    @'
[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

if ($Arguments.Count -eq 1 -and $Arguments[0] -eq "version") {
    Write-Output "8.30.1-test"
    exit 0
}

$reportIndex = [Array]::IndexOf($Arguments, "--report-path")
$reportPath = if ($reportIndex -ge 0) { $Arguments[$reportIndex + 1] } else { $null }
switch ($env:FIAP_FAKE_GITLEAKS_MODE) {
    "no-report" { exit 2 }
    "invalid-json" {
        Set-Content -Path $reportPath -Value "{"
        exit 1
    }
    default {
        Set-Content -Path $reportPath -Value "[]"
        exit 0
    }
}
'@ | Set-Content -Path (Join-Path $tools "gitleaks.ps1")

    return $tools
}

function New-Fixture {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [ValidateSet("none", "clean", "build-failure", "test-failure")]
        [string]$ProjectMode = "none",
        [switch]$ForbiddenFile,
        [switch]$CommittedSecret,
        [switch]$IgnoredSecret
    )

    $root = Join-Path $script:Workspace $Name
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    Set-Content -Path (Join-Path $root "README.md") -Value "# Fixture"
    Set-Content -Path (Join-Path $root ".gitignore") -Value @"
artifacts/
bin/
obj/
.env
"@

    if ($ProjectMode -ne "none") {
        $sources = Join-Path $root "sources"
        New-Item -ItemType Directory -Path $sources -Force | Out-Null
        $isTest = $ProjectMode -in @("clean", "test-failure")
        $projectName = if ($isTest) { "Fixture.Tests.csproj" } else { "Fixture.csproj" }
        $buildFailure = if ($ProjectMode -eq "build-failure") { "<BuildFailure>true</BuildFailure>" } else { "" }
        $testFailure = if ($ProjectMode -eq "test-failure") { "<FailTests>true</FailTests>" } else { "" }
        $testProperty = if ($isTest) { "<IsTestProject>true</IsTestProject>" } else { "" }
        @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    $testProperty
    $buildFailure
    $testFailure
  </PropertyGroup>
</Project>
"@ | Set-Content -Path (Join-Path $sources $projectName)
        Set-Content -Path (Join-Path $sources "Program.cs") -Value "public static class Program { public static void Main() { } }"
    }

    if ($ForbiddenFile) {
        $bin = Join-Path (Join-Path $root "sources") "bin"
        New-Item -ItemType Directory -Path $bin -Force | Out-Null
        Set-Content -Path (Join-Path $bin "Fixture.dll") -Value "not-a-real-binary"
    }

    if ($CommittedSecret) {
        Set-Content -Path (Join-Path $root "appsettings.json") -Value '{ "Password": "e2e-committed-secret" }'
    }

    if ($IgnoredSecret) {
        Set-Content -Path (Join-Path $root ".env") -Value "API_KEY=e2e-local-secret"
    }

    & git -C $root init --quiet
    & git -C $root config user.name "FIAP Quality Tests"
    & git -C $root config user.email "quality-tests@example.invalid"
    & git -C $root add --all --force
    if ($IgnoredSecret) {
        & git -C $root reset --quiet -- .env
    }
    & git -C $root commit --quiet -m "test fixture"
    if ($LASTEXITCODE -ne 0) {
        throw "Não foi possível criar o commit da fixture $Name."
    }

    return $root
}

function Invoke-QualityFixture {
    param(
        [Parameter(Mandatory)]
        [string]$Root,
        [ValidateSet("success", "failure")]
        [string]$ExpectedProcessResult,
        [switch]$Ci,
        [string]$GitleaksMode = "success"
    )

    $previousMode = $env:FIAP_FAKE_GITLEAKS_MODE
    $previousStepSummary = $env:GITHUB_STEP_SUMMARY
    try {
        $env:FIAP_FAKE_GITLEAKS_MODE = $GitleaksMode
        $env:GITHUB_STEP_SUMMARY = $null
        $arguments = @(
            "-NoProfile",
            "-File", $script:QualityScript,
            "-RepositoryRoot", $Root,
            "-DotnetCommand", $script:FakeDotnet,
            "-GitleaksCommand", $script:FakeGitleaks
        )
        if ($Ci) {
            $arguments += "-Ci"
        }
        & pwsh @arguments *> (Join-Path $Root "e2e-output.log")
        $processExitCode = $LASTEXITCODE
    }
    finally {
        $env:FIAP_FAKE_GITLEAKS_MODE = $previousMode
        $env:GITHUB_STEP_SUMMARY = $previousStepSummary
    }

    if ($ExpectedProcessResult -eq "success") {
        Assert-Equal 0 $processExitCode "O processo deveria terminar com sucesso"
    }
    else {
        Assert-True ($processExitCode -ne 0) "O processo deveria falhar"
    }

    $reportDirectory = Join-Path (Join-Path $Root "artifacts") "code-quality"
    $reportPath = Join-Path $reportDirectory "report.json"
    $markdownPath = Join-Path $reportDirectory "report.md"
    Assert-True (Test-Path $reportPath) "report.json não foi gerado"
    Assert-True (Test-Path $markdownPath) "report.md não foi gerado"
    Assert-True (Test-Json -Path $reportPath -SchemaFile $script:SchemaPath) "report.json não respeita o schema"
    Assert-True ((Get-Content -Raw $markdownPath).Contains("# Relatório de qualidade C#")) "O Markdown está incompleto"
    return Get-Content -Raw $reportPath | ConvertFrom-Json
}

$originalPath = $env:PATH
$originalStepSummary = $env:GITHUB_STEP_SUMMARY
try {
    New-Item -ItemType Directory -Path $script:Workspace -Force | Out-Null
    $summaryProbe = Join-Path $script:Workspace "github-step-summary.md"
    Set-Content -Path $summaryProbe -Value "summary-sentinel"
    $env:GITHUB_STEP_SUMMARY = $summaryProbe
    $tools = New-FakeTools
    $script:FakeDotnet = Join-Path $tools "dotnet.ps1"
    $script:FakeGitleaks = Join-Path $tools "gitleaks.ps1"

    Test-Case "Repositório sem projetos não é pontuado" {
        $root = New-Fixture -Name "not-scored"
        $report = Invoke-QualityFixture -Root $root -ExpectedProcessResult success
        Assert-Equal "not-scored" $report.status "Sem projeto o status deve ser not-scored"
        Assert-True ($null -eq $report.score.raw) "Sem projeto não deve existir pontuação bruta"
        Assert-True ($null -eq $report.score.final) "Sem projeto não deve existir pontuação final"
    }

    Test-Case "Fixture limpa produz zero findings" {
        $root = New-Fixture -Name "clean" -ProjectMode clean
        $report = Invoke-QualityFixture -Root $root -ExpectedProcessResult success
        Assert-Equal "passed" $report.status "A fixture limpa deve passar"
        Assert-Equal 100 $report.score.final "A fixture limpa deve obter 100"
        Assert-Equal 0 $report.findings.Count "A fixture limpa não deve produzir findings"
    }

    Test-Case "Falha de build bloqueia e aplica teto 59" {
        $root = New-Fixture -Name "build-failure" -ProjectMode build-failure
        $report = Invoke-QualityFixture -Root $root -ExpectedProcessResult failure
        Assert-Equal "failed" $report.status "Build quebrado deve falhar"
        Assert-Equal 59 $report.score.final "Build quebrado deve aplicar teto 59"
        Assert-True (@($report.findings.id) -contains "FIAP-BUILD") "FIAP-BUILD não foi gerado"
    }

    Test-Case "Arquivo proibido rastreado bloqueia e aplica teto 59" {
        $root = New-Fixture -Name "forbidden" -ProjectMode clean -ForbiddenFile
        $report = Invoke-QualityFixture -Root $root -ExpectedProcessResult failure
        Assert-Equal 59 $report.score.final "Arquivo proibido deve aplicar teto 59"
        Assert-True (@($report.findings.id) -contains "FIAP1001") "FIAP1001 não foi gerado"
    }

    Test-Case "Segredo commitado bloqueia e aplica teto 9" {
        $root = New-Fixture -Name "committed-secret" -ProjectMode clean -CommittedSecret
        $report = Invoke-QualityFixture -Root $root -ExpectedProcessResult failure
        Assert-Equal 9 $report.score.final "Segredo deve aplicar teto 9"
        Assert-True (@($report.findings.id) -contains "FIAP1002") "FIAP1002 não foi gerado"
        $reportPath = Join-Path (Join-Path (Join-Path $root "artifacts") "code-quality") "report.json"
        Assert-True (-not (Get-Content -Raw $reportPath).Contains("e2e-committed-secret")) "O segredo vazou no relatório"
    }

    Test-Case "Segredo ignorado é advisory" {
        $root = New-Fixture -Name "ignored-secret" -ProjectMode clean -IgnoredSecret
        $report = Invoke-QualityFixture -Root $root -ExpectedProcessResult success
        Assert-Equal "passed" $report.status "Segredo ignorado não deve bloquear"
        Assert-True (@($report.findings.id) -contains "FIAP2102") "FIAP2102 não foi gerado"
    }

    Test-Case "Falha de teste existente é advisory" {
        $root = New-Fixture -Name "test-failure" -ProjectMode test-failure
        $report = Invoke-QualityFixture -Root $root -ExpectedProcessResult success
        Assert-Equal "passed" $report.status "Falha de teste não deve bloquear"
        Assert-Equal 96 $report.score.final "FIAP4001 deve descontar quatro pontos"
        Assert-True (@($report.findings.id) -contains "FIAP4001") "FIAP4001 não foi gerado"
    }

    Test-Case "Falha do Gitleaks sem relatório bloqueia no CI" {
        $root = New-Fixture -Name "gitleaks-no-report" -ProjectMode clean
        $report = Invoke-QualityFixture -Root $root -ExpectedProcessResult failure -Ci -GitleaksMode no-report
        $finding = $report.findings | Where-Object id -eq "FIAP0096"
        Assert-True ($null -ne $finding) "FIAP0096 não foi gerado"
        Assert-True $finding.blocking "FIAP0096 deve bloquear no CI"
    }

    Test-Case "JSON inválido do Gitleaks bloqueia no CI" {
        $root = New-Fixture -Name "gitleaks-invalid-json" -ProjectMode clean
        $report = Invoke-QualityFixture -Root $root -ExpectedProcessResult failure -Ci -GitleaksMode invalid-json
        $finding = $report.findings | Where-Object id -eq "FIAP0097"
        Assert-True ($null -ne $finding) "FIAP0097 não foi gerado"
        Assert-True $finding.blocking "FIAP0097 deve bloquear no CI"
    }

    Test-Case "Fixtures E2E não contaminam o Job Summary" {
        Assert-Equal "summary-sentinel" (Get-Content -Raw $summaryProbe).Trim() "O E2E não deve escrever relatórios de fixture no summary"
    }
}
finally {
    $env:PATH = $originalPath
    $env:GITHUB_STEP_SUMMARY = $originalStepSummary
    if (Test-Path $script:Workspace) {
        Remove-Item -Path $script:Workspace -Recurse -Force
    }
}

Write-Host ""
Write-Host "E2E: $script:Passed passaram; $script:Failed falharam."
if ($script:Failed -gt 0) {
    exit 1
}

exit 0
