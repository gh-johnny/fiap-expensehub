[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:Passed = 0
$script:Failed = 0
$script:TestRoot = Join-Path ([System.IO.Path]::GetTempPath()) "fiap-code-quality-unit-$([guid]::NewGuid())"
$repositoryRoot = Split-Path $PSScriptRoot
$scriptUnderTest = Join-Path (Join-Path $repositoryRoot "scripts") "Invoke-CodeQuality.ps1"

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

function Reset-TestState {
    param([string]$Name)

    $caseRoot = Join-Path $script:TestRoot $Name
    New-Item -ItemType Directory -Path $caseRoot -Force | Out-Null
    $script:Root = $caseRoot
    $script:Findings = [System.Collections.Generic.List[object]]::new()
    $script:Projects = [System.Collections.Generic.List[object]]::new()
    $script:Tools = [System.Collections.Generic.List[object]]::new()
    return $caseRoot
}

try {
    New-Item -ItemType Directory -Path $script:TestRoot -Force | Out-Null
    . $scriptUnderTest

    Test-Case "Resolve caminho relativo" {
        $root = Reset-TestState "relative-path"
        $file = Join-Path (Join-Path $root "sources") "Sample.cs"
        New-Item -ItemType Directory -Path (Split-Path $file) -Force | Out-Null
        Set-Content -Path $file -Value "class Sample {}"

        Assert-Equal "sources/Sample.cs" (Get-RelativePath $file) "O caminho deve ser relativo e portátil"
    }

    Test-Case "Lê TargetFramework e TargetFrameworks" {
        $root = Reset-TestState "frameworks"
        $project = Join-Path $root "Sample.csproj"
        @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
  </PropertyGroup>
</Project>
'@ | Set-Content -Path $project

        $frameworks = @(Get-TargetFrameworks $project)
        Assert-Equal 3 $frameworks.Count "Todos os frameworks devem ser retornados"
        Assert-True ($frameworks -contains "net8.0") "net8.0 não foi encontrado"
        Assert-True ($frameworks -contains "net10.0") "net10.0 não foi encontrado"
    }

    Test-Case "Framework antigo gera um único warning" {
        $root = Reset-TestState "unsupported-framework"
        Test-TargetFrameworkSupport -Frameworks @("net7.0", "net8.0") -Project (Join-Path $root "Sample.csproj")

        Assert-Equal 1 $script:Findings.Count "Somente net7.0 deve gerar finding"
        Assert-Equal "FIAP2004" $script:Findings[0].id "A regra de framework deve ser FIAP2004"
    }

    Test-Case "Placeholders de segredo são aceitos" {
        $root = Reset-TestState "secret-placeholders"
        $config = Join-Path $root "appsettings.json"
        @'
{
  "Password": "${DB_PASSWORD}",
  "ApiKey": "${{ secrets.API_KEY }}",
  "Token": "<TOKEN>"
}
'@ | Set-Content -Path $config

        Test-CustomSecrets -Files @("appsettings.json") -BlockingSecrets $true
        Assert-Equal 0 $script:Findings.Count "Placeholders não podem ser classificados como segredo"
    }

    Test-Case "Segredo rastreado bloqueia sem revelar valor" {
        $root = Reset-TestState "tracked-secret"
        $config = Join-Path $root "appsettings.json"
        Set-Content -Path $config -Value '{ "Password": "unit-test-secret-value" }'

        Test-CustomSecrets -Files @("appsettings.json") -BlockingSecrets $true
        Assert-Equal 1 $script:Findings.Count "O segredo deve gerar um finding"
        Assert-Equal "FIAP1002" $script:Findings[0].id "Segredo rastreado deve usar FIAP1002"
        Assert-True $script:Findings[0].blocking "Segredo rastreado deve bloquear"
        Assert-True (-not $script:Findings[0].message.Contains("unit-test-secret-value")) "O valor não pode aparecer na mensagem"
    }

    Test-Case "Segredo local é advisory" {
        $root = Reset-TestState "local-secret"
        $config = Join-Path $root ".env"
        Set-Content -Path $config -Value "API_KEY=unit-test-local-secret"

        Test-CustomSecrets -Files @(".env") -BlockingSecrets $false
        Assert-Equal "FIAP2102" $script:Findings[0].id "Segredo local deve usar FIAP2102"
        Assert-True (-not $script:Findings[0].blocking) "Segredo local não deve bloquear"
    }

    Test-Case "Findings repetidos são agrupados com ocorrências" {
        $root = Reset-TestState "merge"
        $file = Join-Path $root "Sample.cs"
        Add-Finding -Id "FIAP3002" -Source fiap -Category best-practices -Severity warning -Title "Sleep" -Message "Evite Sleep." -File $file -Line 2
        Add-Finding -Id "FIAP3002" -Source fiap -Category best-practices -Severity warning -Title "Sleep" -Message "Evite Sleep." -File $file -Line 7

        Merge-Findings
        Assert-Equal 1 $script:Findings.Count "Findings equivalentes devem ser agrupados"
        Assert-Equal 2 $script:Findings[0].occurrenceCount "As duas localizações devem ser preservadas"
        Assert-Equal 2 $script:Findings[0].locations.Count "As localizações devem permanecer no relatório"
    }

    Test-Case "Desconto é aplicado uma vez por regra" {
        $root = Reset-TestState "deduction-once"
        $file = Join-Path $root "Sample.cs"
        Add-Finding -Id "FIAP3002" -Source fiap -Category best-practices -Severity warning -Title "Sleep" -Message "Primeiro." -File $file -Line 2
        Add-Finding -Id "FIAP3002" -Source fiap -Category best-practices -Severity warning -Title "Sleep" -Message "Segundo." -File $file -Line 7
        Merge-Findings

        $score = Get-Score
        $category = $score.categories | Where-Object id -eq "best-practices"
        Assert-Equal 18 $category.score "FIAP3002 deve descontar somente dois pontos"
        Assert-Equal 1 $category.deductions.Count "Deve existir um único desconto para FIAP3002"
        Assert-Equal 2 $category.deductions[0].occurrences "O desconto deve informar todas as ocorrências"
    }

    Test-Case "Teto de segredo prevalece sobre outros tetos" {
        Reset-TestState "caps" | Out-Null
        Add-Finding -Id "FIAP1002" -Source fiap -Category security -Severity error -Title "Secret" -Message "Secret." -Blocking $true
        Add-Finding -Id "FIAP1001" -Source fiap -Category hygiene -Severity error -Title "Artifact" -Message "Artifact." -Blocking $true
        Add-Finding -Id "FIAP-BUILD" -Source fiap -Category build -Severity error -Title "Build" -Message "Build." -Blocking $true
        Merge-Findings

        $score = Get-Score
        Assert-Equal 9 $score.final "O menor teto deve prevalecer"
        Assert-Equal 3 $score.capsApplied.Count "Todos os tetos aplicáveis devem ser relatados"
    }

    Test-Case "Repositório sem projetos não recebe pontuação" {
        $score = Get-UnscoredScore
        Assert-True ($null -eq $score.raw) "A pontuação bruta deve ser nula"
        Assert-True ($null -eq $score.final) "A pontuação final deve ser nula"
        Assert-Equal 5 $score.categories.Count "As cinco categorias devem permanecer no contrato"
        Assert-True ($null -eq $score.categories[0].score) "Categorias sem projeto não devem receber nota"
    }

    Test-Case "Regras pedagógicas encontram padrões conhecidos" {
        $root = Reset-TestState "custom-rules"
        $sourcesDirectory = Join-Path $root "sources"
        $controllerDirectory = Join-Path $sourcesDirectory "Controllers"
        $modelDirectory = Join-Path $sourcesDirectory "Models"
        $dtoDirectory = Join-Path $sourcesDirectory "DTOs"
        New-Item -ItemType Directory -Path $controllerDirectory, $modelDirectory, $dtoDirectory -Force | Out-Null
        Set-Content -Path (Join-Path $modelDirectory "Student.cs") -Value "public class Student { }"
        Set-Content -Path (Join-Path $dtoDirectory "StudentDto.cs") -Value "public class StudentDto { public string Name { get; set; } }"
        @'
public class StudentsController
{
    public void Create(Student student) { }
    public async Task Check()
    {
        var exists = await students.CountAsync() > 0;
        Thread.Sleep(10);
        var duplicate = _context.Students.Any();
    }
}
'@ | Set-Content -Path (Join-Path $controllerDirectory "StudentsController.cs")
        $files = @(
            "sources/Models/Student.cs",
            "sources/DTOs/StudentDto.cs",
            "sources/Controllers/StudentsController.cs"
        )

        Test-CustomCodePractices -TrackedFiles $files
        $ids = @($script:Findings.id)
        foreach ($expected in @("FIAP3001", "FIAP3002", "FIAP3003", "FIAP3004", "FIAP3005")) {
            Assert-True ($ids -contains $expected) "A regra $expected não foi detectada"
        }
    }

    Test-Case "Entidade de outra aula não gera FIAP3004" {
        $root = Reset-TestState "project-rule-isolation"
        $firstProject = Join-Path (Join-Path $root "sources") "aula-01"
        $secondProject = Join-Path (Join-Path $root "sources") "aula-02"
        $firstModels = Join-Path $firstProject "Models"
        $secondControllers = Join-Path $secondProject "Controllers"
        New-Item -ItemType Directory -Path $firstModels, $secondControllers -Force | Out-Null
        Set-Content -Path (Join-Path $firstProject "Aula01.csproj") -Value '<Project Sdk="Microsoft.NET.Sdk" />'
        Set-Content -Path (Join-Path $secondProject "Aula02.csproj") -Value '<Project Sdk="Microsoft.NET.Sdk" />'
        Set-Content -Path (Join-Path $firstModels "Student.cs") -Value "public class Student { }"
        Set-Content -Path (Join-Path $secondControllers "StudentsController.cs") -Value "public class StudentsController { public void Search(Student student) { } }"
        $files = @(
            "sources/aula-01/Models/Student.cs",
            "sources/aula-02/Controllers/StudentsController.cs"
        )

        Test-CustomCodePractices -TrackedFiles $files
        $ids = @($script:Findings | ForEach-Object { $_.id })
        Assert-True ($ids -notcontains "FIAP3004") "Modelos não podem vazar entre projetos independentes"
    }

    Test-Case "Parser reconhece diagnóstico do compilador" {
        $root = Reset-TestState "compiler-diagnostic"
        $project = Join-Path $root "Sample.csproj"
        $file = Join-Path $root "Program.cs"
        Add-DotnetDiagnostics -Output @("$file(3,7): error CS1002: ; expected [$project]") -Project $project

        Assert-Equal 1 $script:Findings.Count "O diagnóstico deve ser importado"
        Assert-Equal "CS1002" $script:Findings[0].id "O ID do compilador deve ser preservado"
        Assert-Equal "build" $script:Findings[0].category "Erro do compilador pertence a build"
    }

    Test-Case "Falha operacional do Gitleaks bloqueia no CI" {
        Reset-TestState "gitleaks-ci-failure" | Out-Null
        $Ci = $true
        Add-GitleaksOperationalFailure -Id "FIAP0096" -Title "Falha" -Message "Falhou."

        Assert-Equal "error" $script:Findings[0].severity "No CI a falha deve ser erro"
        Assert-True $script:Findings[0].blocking "No CI a falha deve bloquear"
    }

    Test-Case "Falha operacional do Gitleaks é advisory localmente" {
        Reset-TestState "gitleaks-local-failure" | Out-Null
        $Ci = $false
        Add-GitleaksOperationalFailure -Id "FIAP0097" -Title "Falha" -Message "Falhou."

        Assert-Equal "warning" $script:Findings[0].severity "Localmente a falha deve ser warning"
        Assert-True (-not $script:Findings[0].blocking) "Localmente a falha não deve bloquear"
    }
}
finally {
    if (Test-Path $script:TestRoot) {
        Remove-Item -Path $script:TestRoot -Recurse -Force
    }
}

Write-Host ""
Write-Host "Unitários: $script:Passed passaram; $script:Failed falharam."
if ($script:Failed -gt 0) {
    exit 1
}

exit 0
