[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Get-Location).Path,
    [string]$OutputDirectory = "artifacts/code-quality",
    [string]$DotnetCommand = "dotnet",
    [string]$GitleaksCommand = "gitleaks",
    [switch]$Ci,
    [switch]$SkipGitleaks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$script:SchemaVersion = "2.0"
$script:Findings = [System.Collections.Generic.List[object]]::new()
$script:Projects = [System.Collections.Generic.List[object]]::new()
$script:Tools = [System.Collections.Generic.List[object]]::new()

function Resolve-AbsolutePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

function Get-RelativePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    try {
        return [System.IO.Path]::GetRelativePath($script:Root, $Path).Replace("\", "/")
    }
    catch {
        return $Path.Replace("\", "/")
    }
}

function Add-Finding {
    param(
        [Parameter(Mandatory)]
        [string]$Id,
        [Parameter(Mandatory)]
        [ValidateSet("compiler", "analyzer", "format", "gitleaks", "fiap", "test")]
        [string]$Source,
        [Parameter(Mandatory)]
        [ValidateSet("build", "security", "hygiene", "style", "best-practices")]
        [string]$Category,
        [Parameter(Mandatory)]
        [ValidateSet("error", "warning", "info")]
        [string]$Severity,
        [Parameter(Mandatory)]
        [string]$Title,
        [Parameter(Mandatory)]
        [string]$Message,
        [string]$File,
        [int]$Line = 0,
        [int]$Column = 0,
        [string]$Project,
        [string]$Recommendation,
        [string]$DocumentationUrl,
        [bool]$Blocking = $false
    )

    $script:Findings.Add([pscustomobject][ordered]@{
        id = $Id
        source = $Source
        category = $Category
        severity = $Severity
        blocking = $Blocking
        title = $Title
        message = $Message
        file = if ($File) { Get-RelativePath $File } else { $null }
        line = if ($Line -gt 0) { $Line } else { $null }
        column = if ($Column -gt 0) { $Column } else { $null }
        project = if ($Project) { Get-RelativePath $Project } else { $null }
        recommendation = if ($Recommendation) { $Recommendation } else { $null }
        documentationUrl = if ($DocumentationUrl) { $DocumentationUrl } else { $null }
    })
}

function Invoke-CapturedCommand {
    param(
        [Parameter(Mandatory)]
        [string]$Command,
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$LogPath,
        [string]$WorkingDirectory = $script:Root
    )

    $previousLocation = Get-Location
    try {
        Set-Location $WorkingDirectory
        $output = @(& $Command @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
        $output | ForEach-Object { "$_" } | Set-Content -Path $LogPath -Encoding utf8
        return [pscustomobject]@{
            exitCode = $exitCode
            output = @($output | ForEach-Object { "$_" })
            logPath = Get-RelativePath $LogPath
        }
    }
    finally {
        Set-Location $previousLocation
    }
}

function Add-DotnetDiagnostics {
    param(
        [string[]]$Output,
        [string]$Project
    )

    $diagnosticPattern = "^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s+(?<severity>warning|error)\s+(?<id>[A-Za-z]+\d+):\s+(?<message>.+?)(?:\s+\[(?<project>.+)\])?$"
    $projectPattern = "^(?<prefix>.*?):\s+(?<severity>warning|error)\s+(?<id>[A-Za-z]+\d+):\s+(?<message>.+?)(?:\s+\[(?<project>.+)\])?$"

    foreach ($entry in $Output) {
        $text = "$entry".Trim()
        $match = [regex]::Match($text, $diagnosticPattern)
        if ($match.Success) {
            $id = $match.Groups["id"].Value.ToUpperInvariant()
            $source = if ($id.StartsWith("CA") -or $id.StartsWith("IDE")) { "analyzer" } else { "compiler" }
            $category = Get-CategoryForDiagnostic $id
            Add-Finding `
                -Id $id `
                -Source $source `
                -Category $category `
                -Severity $match.Groups["severity"].Value `
                -Title "Diagnóstico $id" `
                -Message $match.Groups["message"].Value.Trim() `
                -File $match.Groups["file"].Value `
                -Line ([int]$match.Groups["line"].Value) `
                -Column ([int]$match.Groups["column"].Value) `
                -Project $Project `
                -Recommendation (Get-Recommendation $id) `
                -DocumentationUrl (Get-DocumentationUrl $id)
            continue
        }

        $match = [regex]::Match($text, $projectPattern)
        if ($match.Success) {
            $id = $match.Groups["id"].Value.ToUpperInvariant()
            $source = if ($id.StartsWith("CA") -or $id.StartsWith("IDE")) { "analyzer" } else { "compiler" }
            Add-Finding `
                -Id $id `
                -Source $source `
                -Category (Get-CategoryForDiagnostic $id) `
                -Severity $match.Groups["severity"].Value `
                -Title "Diagnóstico $id" `
                -Message $match.Groups["message"].Value.Trim() `
                -Project $Project `
                -Recommendation (Get-Recommendation $id) `
                -DocumentationUrl (Get-DocumentationUrl $id)
        }
    }
}

function Get-CategoryForDiagnostic {
    param([string]$Id)

    if ($Id -match "^CA(21|30|53|54)") {
        return "security"
    }

    if ($Id.StartsWith("IDE")) {
        return "style"
    }

    if ($Id.StartsWith("CA")) {
        return "best-practices"
    }

    return "build"
}

function Get-Recommendation {
    param([string]$Id)

    $recommendations = @{
        "CA1305" = "Informe explicitamente a cultura ou o format provider."
        "CA1828" = "Use AnyAsync para verificar existência sem contar todos os registros."
        "CA1849" = "Use a alternativa assíncrona disponível dentro do fluxo async."
        "CA2000" = "Descarte corretamente objetos que implementam IDisposable."
        "CA2016" = "Propague o CancellationToken recebido para as chamadas internas."
        "CA2100" = "Não construa comandos SQL concatenando entrada externa."
        "CA2208" = "Informe o parâmetro correto ao criar a exceção."
        "CA5350" = "Substitua o algoritmo criptográfico obsoleto por uma alternativa segura."
        "CA5394" = "Não use aleatoriedade previsível em decisões de segurança."
        "IDE0005" = "Remova diretivas using que não são utilizadas."
        "IDE0044" = "Marque o campo como readonly quando ele não muda após a construção."
        "IDE0055" = "Aplique a formatação definida no .editorconfig."
        "IDE1006" = "Renomeie o símbolo conforme o padrão definido no .editorconfig."
    }

    if ($recommendations.ContainsKey($Id)) {
        return $recommendations[$Id]
    }

    return "Consulte a mensagem do diagnóstico e ajuste o código indicado."
}

function Get-DocumentationUrl {
    param([string]$Id)

    if ($Id.StartsWith("CA")) {
        return "https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/$($Id.ToLowerInvariant())"
    }

    if ($Id.StartsWith("IDE")) {
        return "https://learn.microsoft.com/dotnet/fundamentals/code-analysis/style-rules/$($Id.ToLowerInvariant())"
    }

    return $null
}

function Get-PropertyValue {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Add-FormatDiagnostics {
    param(
        [string]$ReportPath,
        [string]$Project
    )

    if (-not (Test-Path $ReportPath)) {
        return
    }

    try {
        $documents = @(Get-Content -Raw $ReportPath | ConvertFrom-Json)
        foreach ($document in $documents) {
            $filePath = Get-PropertyValue $document "FilePath"
            if (-not $filePath) {
                $filePath = Get-PropertyValue $document "DocumentPath"
            }

            $changes = @(Get-PropertyValue $document "FileChanges")
            if ($changes.Count -eq 0) {
                $changes = @(Get-PropertyValue $document "Diagnostics")
            }

            foreach ($change in $changes) {
                $id = Get-PropertyValue $change "DiagnosticId"
                if (-not $id) {
                    $id = "IDE0055"
                }
                $normalizedId = ("$id").ToUpperInvariant()
                if ($normalizedId.StartsWith("CA") -or $normalizedId -match "^(CS|BC|FS)\d+") {
                    continue
                }

                $line = Get-PropertyValue $change "LineNumber"
                if (-not $line) {
                    $line = Get-PropertyValue $change "Line"
                }

                $column = Get-PropertyValue $change "CharNumber"
                if (-not $column) {
                    $column = Get-PropertyValue $change "Column"
                }

                $description = Get-PropertyValue $change "FormatDescription"
                if (-not $description) {
                    $description = Get-PropertyValue $change "Message"
                }
                if (-not $description) {
                    $description = "O arquivo não segue a formatação configurada."
                }

                Add-Finding `
                    -Id $normalizedId `
                    -Source "format" `
                    -Category "style" `
                    -Severity "warning" `
                    -Title "Formatação ou estilo" `
                    -Message "$description" `
                    -File $filePath `
                    -Line ([int]($line ?? 0)) `
                    -Column ([int]($column ?? 0)) `
                    -Project $Project `
                    -Recommendation (Get-Recommendation $normalizedId) `
                    -DocumentationUrl (Get-DocumentationUrl $normalizedId)
            }
        }
    }
    catch {
        Add-Finding `
            -Id "FIAP0098" `
            -Source "fiap" `
            -Category "style" `
            -Severity "info" `
            -Title "Relatório de formatação não pôde ser lido" `
            -Message $_.Exception.Message `
            -Project $Project `
            -Recommendation "Consulte o relatório bruto do dotnet format."
    }
}

function Merge-Findings {
    $merged = [System.Collections.Generic.List[object]]::new()
    $groups = $script:Findings | Group-Object {
        @(
            $_.id,
            $_.source,
            $_.category,
            $_.severity,
            $_.blocking,
            $_.file,
            $_.project,
            $_.message
        ) -join [char]31
    }

    foreach ($group in $groups) {
        $first = $group.Group[0]
        $locations = @($group.Group | ForEach-Object {
            [pscustomobject][ordered]@{
                line = $_.line
                column = $_.column
            }
        } | Sort-Object line, column -Unique)
        $merged.Add([pscustomobject][ordered]@{
            id = $first.id
            source = $first.source
            category = $first.category
            severity = $first.severity
            blocking = $first.blocking
            title = $first.title
            message = $first.message
            file = $first.file
            line = $locations[0].line
            column = $locations[0].column
            project = $first.project
            occurrenceCount = $locations.Count
            locations = $locations
            recommendation = $first.recommendation
            documentationUrl = $first.documentationUrl
        })
    }

    $script:Findings = $merged
}

function Get-TargetFrameworks {
    param([string]$ProjectPath)

    try {
        [xml]$projectXml = Get-Content -Raw $ProjectPath
        $values = [System.Collections.Generic.List[string]]::new()
        foreach ($propertyGroup in @($projectXml.Project.PropertyGroup)) {
            if ($propertyGroup.TargetFramework) {
                $values.Add("$($propertyGroup.TargetFramework)")
            }
            if ($propertyGroup.TargetFrameworks) {
                foreach ($framework in "$($propertyGroup.TargetFrameworks)".Split(";")) {
                    if (-not [string]::IsNullOrWhiteSpace($framework)) {
                        $values.Add($framework.Trim())
                    }
                }
            }
        }
        return @($values | Select-Object -Unique)
    }
    catch {
        return @()
    }
}

function Test-TargetFrameworkSupport {
    param(
        [string[]]$Frameworks,
        [string]$Project
    )

    foreach ($framework in $Frameworks) {
        $match = [regex]::Match($framework, "^net(?<major>\d+)\.")
        if (-not $match.Success) {
            continue
        }

        $major = [int]$match.Groups["major"].Value
        if ($major -lt 8 -or $major -gt 10) {
            Add-Finding `
                -Id "FIAP2004" `
                -Source "fiap" `
                -Category "best-practices" `
                -Severity "warning" `
                -Title "Versão do .NET fora do suporte" `
                -Message "O projeto usa $framework, que não recebe mais suporte oficial." `
                -Project $Project `
                -Recommendation "Migre para uma versão suportada do .NET quando a atividade permitir." `
                -DocumentationUrl "https://dotnet.microsoft.com/platform/support/policy/dotnet-core"
        }
    }
}

function Get-TrackedFiles {
    $insideGit = $false
    try {
        $insideGit = ((& git -C $script:Root rev-parse --is-inside-work-tree 2>$null) -eq "true")
    }
    catch {
        $insideGit = $false
    }

    if (-not $insideGit) {
        Add-Finding `
            -Id "FIAP0001" `
            -Source "fiap" `
            -Category "hygiene" `
            -Severity "info" `
            -Title "Diretório sem repositório Git" `
            -Message "As verificações de arquivos rastreados foram executadas sobre os arquivos existentes." `
            -Recommendation "Inicialize e versione o repositório para obter a análise completa."
        return @(Get-ChildItem -Path $script:Root -File -Recurse -Force | ForEach-Object {
            Get-RelativePath $_.FullName
        })
    }

    return @(& git -C $script:Root ls-files | ForEach-Object { "$_".Replace("\", "/") })
}

function Get-AnalysisFiles {
    param([string[]]$TrackedFiles)

    $existingFiles = @(Get-ChildItem -Path $script:Root -File -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notmatch "[\\/](\.git|artifacts|bin|obj|\.vs)[\\/]"
        } |
        ForEach-Object { Get-RelativePath $_.FullName })

    return @($TrackedFiles + $existingFiles | Sort-Object -Unique)
}

function Test-RepositoryHygiene {
    param([string[]]$TrackedFiles)

    $forbiddenPattern = "(^|/)(bin|obj|\.vs)(/|$)|\.(dll|exe|pdb|suo|userosscache|sln\.docstates)$"
    $forbiddenGroups = @{}
    foreach ($file in $TrackedFiles) {
        if ($file -match $forbiddenPattern) {
            $directoryMatch = [regex]::Match($file, "^(?<prefix>.*?)(?<directory>bin|obj|\.vs)(?:/|$)")
            $groupKey = if ($directoryMatch.Success) {
                "$($directoryMatch.Groups['prefix'].Value)$($directoryMatch.Groups['directory'].Value)/"
            }
            else {
                $file
            }
            if (-not $forbiddenGroups.ContainsKey($groupKey)) {
                $forbiddenGroups[$groupKey] = [System.Collections.Generic.List[string]]::new()
            }
            $forbiddenGroups[$groupKey].Add($file)
        }
    }

    foreach ($groupKey in $forbiddenGroups.Keys | Sort-Object) {
        $files = $forbiddenGroups[$groupKey]
        $message = if ($files.Count -eq 1) {
            "O arquivo pertence a build ou estado local e não deve ser versionado."
        }
        else {
            "O diretório contém $($files.Count) arquivos gerados que não devem ser versionados."
        }
        Add-Finding `
            -Id "FIAP1001" `
            -Source "fiap" `
            -Category "hygiene" `
            -Severity "error" `
            -Title "Arquivo gerado rastreado pelo Git" `
            -Message $message `
            -File (Join-Path $script:Root $groupKey) `
            -Recommendation "Remova os arquivos do índice do Git e mantenha a regra correspondente no .gitignore." `
            -Blocking $true
    }

    if (-not (Test-Path (Join-Path $script:Root ".gitignore"))) {
        Add-Finding `
            -Id "FIAP2002" `
            -Source "fiap" `
            -Category "hygiene" `
            -Severity "warning" `
            -Title ".gitignore ausente" `
            -Message "O repositório não possui um .gitignore na raiz." `
            -Recommendation "Adicione o .gitignore do template para impedir binários, caches e segredos."
    }

    $readmeExists = @(@("README.md", "README.txt") | Where-Object {
        Test-Path (Join-Path $script:Root $_)
    })
    if ($readmeExists.Count -eq 0) {
        Add-Finding `
            -Id "FIAP2003" `
            -Source "fiap" `
            -Category "hygiene" `
            -Severity "warning" `
            -Title "README ausente" `
            -Message "O repositório não possui documentação inicial." `
            -Recommendation "Documente objetivo, execução e estrutura do projeto em README.md."
    }

    foreach ($file in $TrackedFiles) {
        if ($file -match "(^|/)(WeatherForecast|WeatherForecastController)\.cs$") {
            Add-Finding `
                -Id "FIAP2005" `
                -Source "fiap" `
                -Category "hygiene" `
                -Severity "warning" `
                -Title "Scaffolding padrão não removido" `
                -Message "O exemplo WeatherForecast do template ainda está versionado." `
                -File (Join-Path $script:Root $file) `
                -Recommendation "Remova exemplos que não fazem parte da solução entregue."
        }

        if ($file -match "(^|/)(usuarios|produtos)\.json$") {
            Add-Finding `
                -Id "FIAP2006" `
                -Source "fiap" `
                -Category "hygiene" `
                -Severity "warning" `
                -Title "Arquivo de dados de execução versionado" `
                -Message "Dados gerados pela execução podem conter informações locais ou sensíveis." `
                -File (Join-Path $script:Root $file) `
                -Recommendation "Versione somente dados de exemplo intencionais e sem informações sensíveis."
        }
    }
}

function Test-CustomSecrets {
    param(
        [string[]]$Files,
        [bool]$BlockingSecrets
    )

    $textExtensions = @(".json", ".yml", ".yaml", ".xml", ".config", ".props", ".targets", ".cs", ".env")
    $sensitiveKeyPattern = "(?i)(password|passwd|pwd|senha|secret|token|api[_-]?key|connectionstring)"
    $placeholderPattern = "(?i)^(\s*|\*+|x+|changeme|example|sample|your[-_ ].*|replace[-_ ].*|<.*>|\$\{.*\}|\$\{\{.*\}\}|\$\(.*\)|%\w+%)$"

    foreach ($relativePath in $Files) {
        if ($relativePath -match "(^|/)(bin|obj|\.vs)(/|$)|\.(dll|exe|pdb)$") {
            continue
        }

        $fullPath = Join-Path $script:Root $relativePath
        if (-not (Test-Path $fullPath -PathType Leaf)) {
            continue
        }

        $extension = [System.IO.Path]::GetExtension($fullPath).ToLowerInvariant()
        if ($textExtensions -notcontains $extension) {
            continue
        }

        $fileInfo = Get-Item $fullPath -Force
        if ($fileInfo.Length -gt 1MB) {
            continue
        }

        $lineNumber = 0
        foreach ($line in Get-Content $fullPath) {
            $lineNumber++
            if ($line -notmatch $sensitiveKeyPattern) {
                continue
            }

            $valuePattern = if ($extension -eq ".cs") {
                "(?i)(?:password|passwd|pwd|senha|secret|token|api[_-]?key)\s*=\s*`"(?<value>[^`"]{4,})`""
            }
            else {
                "(?i)(?:password|passwd|pwd|senha|secret|token|api[_-]?key|connectionstrings?)[`"']?\s*[=:]\s*(?:`"(?<double>[^`"]*)`"|'(?<single>[^']*)'|(?<bare>[^,;}\s]+))"
            }
            $valueMatch = [regex]::Match($line, $valuePattern)
            if (-not $valueMatch.Success) {
                continue
            }

            $value = if ($extension -eq ".cs") {
                $valueMatch.Groups["value"].Value
            }
            elseif ($valueMatch.Groups["double"].Success) {
                $valueMatch.Groups["double"].Value
            }
            elseif ($valueMatch.Groups["single"].Success) {
                $valueMatch.Groups["single"].Value
            }
            else {
                $valueMatch.Groups["bare"].Value
            }
            $value = $value.Trim()
            if ($value -eq "{") {
                continue
            }
            if ($value -match $placeholderPattern) {
                continue
            }

            $findingId = if ($BlockingSecrets) { "FIAP1002" } else { "FIAP2102" }
            $severity = if ($BlockingSecrets) { "error" } else { "warning" }
            $title = if ($BlockingSecrets) {
                "Possível segredo versionado"
            }
            else {
                "Possível segredo em arquivo local"
            }
            Add-Finding `
                -Id $findingId `
                -Source "fiap" `
                -Category "security" `
                -Severity $severity `
                -Title $title `
                -Message "Foi encontrado um campo sensível com valor preenchido. O valor foi ocultado." `
                -File $fullPath `
                -Line $lineNumber `
                -Recommendation "Use variáveis de ambiente, User Secrets ou GitHub Secrets e não adicione este valor ao Git." `
                -Blocking $BlockingSecrets
        }
    }
}

function Test-PlaintextPasswordPersistence {
    param([string[]]$Files)

    $passwordPropertyFound = $false
    $plainPersistenceFound = $false
    foreach ($relativePath in $Files | Where-Object { $_ -like "*.cs" }) {
        $fullPath = Join-Path $script:Root $relativePath
        if (-not (Test-Path $fullPath)) {
            continue
        }
        $content = Get-Content -Raw $fullPath
        if ($content -match "(?i)\b(string)\s+(Senha|Password)\s*\{") {
            $passwordPropertyFound = $true
        }
        if ($content -match "JsonSerializer\.Serialize" -and $content -match "File\.WriteAllText") {
            $plainPersistenceFound = $true
        }
    }

    if ($passwordPropertyFound -and $plainPersistenceFound) {
        Add-Finding `
            -Id "FIAP2101" `
            -Source "fiap" `
            -Category "security" `
            -Severity "warning" `
            -Title "Possível persistência de senha em texto puro" `
            -Message "O projeto possui campo de senha e serialização direta para arquivo." `
            -Recommendation "Armazene apenas hash seguro da senha e nunca serialize a credencial em texto puro."
    }
}

function Test-CustomCodePractices {
    param([string[]]$TrackedFiles)

    $allSourceFiles = @($TrackedFiles | Where-Object {
        $_ -like "*.cs" -and
        $_ -notmatch "(^|/)(bin|obj|Migrations)(/|$)"
    })

    $projectDirectories = @(Get-ChildItem -Path (Join-Path $script:Root "sources") -Filter "*.csproj" -File -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object { Get-RelativePath $_.DirectoryName } |
        Sort-Object Length -Descending)
    $sourceGroups = $allSourceFiles | Group-Object {
        $relativePath = $_
        $projectDirectory = $projectDirectories | Where-Object {
            $relativePath -eq $_ -or $relativePath.StartsWith("$_/", [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if ($projectDirectory) { $projectDirectory } else { "." }
    }

    foreach ($sourceGroup in $sourceGroups) {
        $sourceFiles = @($sourceGroup.Group)
        $modelNames = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::Ordinal
        )
        foreach ($relativePath in $sourceFiles | Where-Object { $_ -match "(^|/)Models?/" }) {
            $fullPath = Join-Path $script:Root $relativePath
            $content = Get-Content -Raw $fullPath
            foreach ($match in [regex]::Matches($content, "\b(?:class|record|struct)\s+(?<name>[A-Za-z_]\w*)")) {
                $null = $modelNames.Add($match.Groups["name"].Value)
            }
        }

        foreach ($relativePath in $sourceFiles) {
            $fullPath = Join-Path $script:Root $relativePath
            $lineNumber = 0
            foreach ($line in Get-Content $fullPath) {
                $lineNumber++

                if ($line -match "\bCountAsync\s*\([^;]*\)\s*>\s*0") {
                    Add-Finding `
                        -Id "FIAP3001" `
                        -Source "fiap" `
                        -Category "best-practices" `
                        -Severity "warning" `
                        -Title "Contagem usada para verificar existência" `
                        -Message "CountAsync > 0 consulta uma contagem quando apenas a existência é necessária." `
                        -File $fullPath `
                        -Line $lineNumber `
                        -Recommendation "Use AnyAsync com o mesmo predicado."
                }

                if ($line -match "\bThread\.Sleep\s*\(") {
                    Add-Finding `
                        -Id "FIAP3002" `
                        -Source "fiap" `
                        -Category "best-practices" `
                        -Severity "warning" `
                        -Title "Thread bloqueada por Sleep" `
                        -Message "Thread.Sleep bloqueia a thread durante a espera." `
                        -File $fullPath `
                        -Line $lineNumber `
                        -Recommendation "Em fluxo assíncrono, prefira await Task.Delay. Em console simples, avalie se a espera é realmente necessária."
                }

                if ($line -match "_context\.\w+\.Any\s*\(") {
                    Add-Finding `
                        -Id "FIAP3003" `
                        -Source "fiap" `
                        -Category "best-practices" `
                        -Severity "warning" `
                        -Title "Consulta síncrona ao banco" `
                        -Message "A consulta Any síncrona pode bloquear uma requisição assíncrona." `
                        -File $fullPath `
                        -Line $lineNumber `
                        -Recommendation "Use AnyAsync e aguarde o resultado com await."
                }
            }

            if ($relativePath -match "(^|/)Controllers?/") {
                $content = Get-Content -Raw $fullPath
                foreach ($modelName in $modelNames) {
                    $signaturePattern = "\bpublic\s+(?:async\s+)?[^{;\r\n]+\([^)]*\b$([regex]::Escape($modelName))\s+\w+"
                    if ($content -match $signaturePattern) {
                        Add-Finding `
                            -Id "FIAP3004" `
                            -Source "fiap" `
                            -Category "best-practices" `
                            -Severity "warning" `
                            -Title "Entidade recebida diretamente pela API" `
                            -Message "O controller recebe a entidade $modelName diretamente, aumentando o risco de overposting e acoplamento." `
                            -File $fullPath `
                            -Recommendation "Crie um DTO específico para os campos aceitos pela operação."
                    }
                }
            }

            if ($relativePath -match "(^|/)DTOs?/") {
                $content = Get-Content -Raw $fullPath
                if ($content -notmatch "\[(Required|Range|StringLength|MinLength|MaxLength|EmailAddress|RegularExpression)\b") {
                    Add-Finding `
                        -Id "FIAP3005" `
                        -Source "fiap" `
                        -Category "best-practices" `
                        -Severity "warning" `
                        -Title "DTO sem validação declarativa" `
                        -Message "O DTO não possui atributos de validação para os dados recebidos." `
                        -File $fullPath `
                        -Recommendation "Adicione validações coerentes com o domínio, como Required, Range ou StringLength."
                }
            }
        }
    }
}

function Add-GitleaksOperationalFailure {
    param(
        [Parameter(Mandatory)]
        [string]$Id,
        [Parameter(Mandatory)]
        [string]$Title,
        [Parameter(Mandatory)]
        [string]$Message
    )

    Add-Finding `
        -Id $Id `
        -Source "fiap" `
        -Category "security" `
        -Severity $(if ($Ci) { "error" } else { "warning" }) `
        -Title $Title `
        -Message $Message `
        -Recommendation "Consulte o log bruto do Gitleaks e repita a análise." `
        -Blocking ([bool]$Ci)
}

function Invoke-Gitleaks {
    if ($SkipGitleaks) {
        Add-Finding `
            -Id "FIAP0002" `
            -Source "fiap" `
            -Category "security" `
            -Severity "info" `
            -Title "Gitleaks ignorado" `
            -Message "A execução foi iniciada com -SkipGitleaks." `
            -Recommendation "Execute sem essa opção antes de enviar o código."
        return
    }

    $gitleaks = Get-Command $GitleaksCommand -ErrorAction SilentlyContinue
    if (-not $gitleaks) {
        Add-Finding `
            -Id "FIAP0003" `
            -Source "fiap" `
            -Category "security" `
            -Severity "warning" `
            -Title "Gitleaks não instalado" `
            -Message "A análise local continuou com verificações próprias. O CI executará o Gitleaks." `
            -Recommendation "Instale o Gitleaks para reproduzir localmente a análise completa."
        $script:Tools.Add([pscustomobject]@{
            name = "gitleaks"
            version = $null
            available = $false
        })
        return
    }

    $versionOutput = @(& $GitleaksCommand version 2>$null)
    $script:Tools.Add([pscustomobject]@{
        name = "gitleaks"
        version = ($versionOutput -join " ").Trim()
        available = $true
    })

    $reportPath = Join-Path $script:RawDirectory "gitleaks.json"
    $logPath = Join-Path $script:LogDirectory "gitleaks.log"
    $result = Invoke-CapturedCommand `
        -Command $GitleaksCommand `
        -Arguments @(
            "git",
            "--redact",
            "--report-format", "json",
            "--report-path", $reportPath,
            $script:Root
        ) `
        -LogPath $logPath

    if (Test-Path $reportPath) {
        try {
            $leaks = @(Get-Content -Raw $reportPath | ConvertFrom-Json)
            foreach ($leak in $leaks) {
                $file = Get-PropertyValue $leak "File"
                $line = Get-PropertyValue $leak "StartLine"
                $ruleId = Get-PropertyValue $leak "RuleID"
                $description = Get-PropertyValue $leak "Description"
                Add-Finding `
                    -Id "GITLEAKS:$ruleId" `
                    -Source "gitleaks" `
                    -Category "security" `
                    -Severity "error" `
                    -Title "Segredo detectado pelo Gitleaks" `
                    -Message "$description O valor foi ocultado." `
                    -File (Join-Path $script:Root $file) `
                    -Line ([int]($line ?? 0)) `
                    -Recommendation "Revogue o segredo, remova-o do histórico e use um mecanismo seguro de configuração." `
                    -DocumentationUrl "https://github.com/gitleaks/gitleaks" `
                    -Blocking $true
            }
        }
        catch {
            Add-GitleaksOperationalFailure `
                -Id "FIAP0097" `
                -Title "Relatório do Gitleaks não pôde ser lido" `
                -Message $_.Exception.Message
        }
    }
    elseif ($result.exitCode -ne 0) {
        Add-GitleaksOperationalFailure `
            -Id "FIAP0096" `
            -Title "Falha ao executar Gitleaks" `
            -Message "O Gitleaks terminou com código $($result.exitCode), sem relatório JSON."
    }
}

function Get-DeductionForRule {
    param(
        [string]$Id,
        [string]$Category
    )

    $fixed = @{
        "FIAP-BUILD" = 20
        "FIAP1001" = 20
        "FIAP1002" = 20
        "FIAP2002" = 4
        "FIAP2003" = 2
        "FIAP2004" = 2
        "FIAP2005" = 2
        "FIAP2006" = 2
        "FIAP2101" = 5
        "FIAP2102" = 2
        "FIAP3001" = 2
        "FIAP3002" = 2
        "FIAP3003" = 2
        "FIAP3004" = 2
        "FIAP3005" = 2
        "FIAP4001" = 4
    }

    if ($Id.StartsWith("GITLEAKS:")) {
        return 20
    }
    if ($fixed.ContainsKey($Id)) {
        return $fixed[$Id]
    }
    if ($Id.StartsWith("IDE")) {
        return 1
    }
    if ($Id.StartsWith("CA")) {
        if ($Category -eq "security") {
            return 4
        }
        return 2
    }
    if ($Id -match "^(CS|BC|FS)\d+") {
        return 20
    }

    return 0
}

function Get-Score {
    $categoryNames = @("build", "security", "hygiene", "style", "best-practices")
    $categories = [System.Collections.Generic.List[object]]::new()

    foreach ($category in $categoryNames) {
        $deductions = [System.Collections.Generic.List[object]]::new()
        $groups = $script:Findings |
            Where-Object { $_.category -eq $category } |
            Group-Object id

        $totalDeduction = 0
        foreach ($group in $groups) {
            $points = Get-DeductionForRule -Id $group.Name -Category $category
            if ($points -le 0) {
                continue
            }

            $totalDeduction += $points
            $deductions.Add([pscustomobject][ordered]@{
                ruleId = $group.Name
                points = $points
                occurrences = ($group.Group | Measure-Object -Property occurrenceCount -Sum).Sum
            })
        }

        $score = [Math]::Max(0, 20 - $totalDeduction)
        $categories.Add([pscustomobject][ordered]@{
            id = $category
            maximum = 20
            score = $score
            deductions = @($deductions)
        })
    }

    $rawScore = ($categories | Measure-Object -Property score -Sum).Sum
    $caps = [System.Collections.Generic.List[object]]::new()

    $hasSecret = @($script:Findings | Where-Object {
        $_.id -eq "FIAP1002" -or $_.id.StartsWith("GITLEAKS:")
    }).Count -gt 0
    $hasBuildFailure = @($script:Findings | Where-Object { $_.id -eq "FIAP-BUILD" }).Count -gt 0
    $hasForbiddenFile = @($script:Findings | Where-Object { $_.id -eq "FIAP1001" }).Count -gt 0

    if ($hasSecret) {
        $caps.Add([pscustomobject]@{ reason = "secret-detected"; maximum = 9 })
    }
    if ($hasBuildFailure) {
        $caps.Add([pscustomobject]@{ reason = "build-failed"; maximum = 59 })
    }
    if ($hasForbiddenFile) {
        $caps.Add([pscustomobject]@{ reason = "forbidden-file-tracked"; maximum = 59 })
    }

    $finalScore = $rawScore
    if ($caps.Count -gt 0) {
        $lowestCap = ($caps | Measure-Object -Property maximum -Minimum).Minimum
        $finalScore = [Math]::Min($rawScore, $lowestCap)
    }

    return [pscustomobject][ordered]@{
        raw = [int]$rawScore
        final = [int]$finalScore
        maximum = 100
        categories = @($categories)
        capsApplied = @($caps)
    }
}

function Get-UnscoredScore {
    $categories = @("build", "security", "hygiene", "style", "best-practices") | ForEach-Object {
        [pscustomobject][ordered]@{
            id = $_
            maximum = 20
            score = $null
            deductions = @()
        }
    }

    return [pscustomobject][ordered]@{
        raw = $null
        final = $null
        maximum = 100
        categories = @($categories)
        capsApplied = @()
    }
}

function Escape-GitHubCommandValue {
    param([string]$Value)

    return $Value.
        Replace("%", "%25").
        Replace("`r", "%0D").
        Replace("`n", "%0A")
}

function Write-GitHubAnnotations {
    if (-not $Ci) {
        return
    }

    $limit = 50
    $count = 0
    foreach ($finding in $script:Findings | Where-Object { $_.severity -ne "info" }) {
        if ($count -ge $limit) {
            Write-Output "::notice::Outros findings estão disponíveis nos artefatos do workflow."
            break
        }

        $command = if ($finding.severity -eq "error") { "error" } else { "warning" }
        $properties = [System.Collections.Generic.List[string]]::new()
        if ($finding.file) {
            $properties.Add("file=$(Escape-GitHubCommandValue $finding.file)")
        }
        if ($finding.line) {
            $properties.Add("line=$($finding.line)")
        }
        if ($finding.column) {
            $properties.Add("col=$($finding.column)")
        }
        $propertyText = if ($properties.Count -gt 0) { " " + ($properties -join ",") } else { "" }
        $message = Escape-GitHubCommandValue "[$($finding.id)] $($finding.message)"
        Write-Output "::$command$propertyText::$message"
        $count++
    }
}

function Write-MarkdownReport {
    param(
        [object]$Report,
        [string]$Path
    )

    $lines = [System.Collections.Generic.List[string]]::new()
    $scoreText = if ($null -eq $Report.score.final) { "Não avaliada" } else { "$($Report.score.final)/100" }
    $lines.Add("# Relatório de qualidade C#")
    $lines.Add("")
    $lines.Add("- **Status:** $($Report.status)")
    $lines.Add("- **Pontuação:** $scoreText")
    $lines.Add("- **Commit:** $($Report.repository.commit)")
    $lines.Add("- **Gerado em:** $($Report.generatedAtUtc)")
    $lines.Add("")

    if ($Report.score.capsApplied.Count -gt 0) {
        $lines.Add("## Tetos aplicados")
        $lines.Add("")
        foreach ($cap in $Report.score.capsApplied) {
            $lines.Add(("- ``{0}``: máximo {1}" -f $cap.reason, $cap.maximum))
        }
        $lines.Add("")
    }

    $lines.Add("## Categorias")
    $lines.Add("")
    $lines.Add("| Categoria | Pontuação | Descontos únicos |")
    $lines.Add("|---|---:|---:|")
    foreach ($category in $Report.score.categories) {
        $categoryScore = if ($null -eq $category.score) { "N/A" } else { "$($category.score)/20" }
        $lines.Add("| $($category.id) | $categoryScore | $($category.deductions.Count) |")
    }
    $lines.Add("")

    $blocking = @($Report.findings | Where-Object { $_.blocking })
    $lines.Add("## Bloqueantes")
    $lines.Add("")
    if ($blocking.Count -eq 0) {
        $lines.Add("Nenhum bloqueante encontrado.")
    }
    else {
        foreach ($finding in $blocking) {
            $location = if ($finding.file) {
                "$($finding.file)" + $(if ($finding.line) { ":$($finding.line)" } else { "" })
            }
            else {
                "repositório"
            }
            $lines.Add("- **$($finding.id)** em ``$location``: $($finding.message)")
        }
    }
    $lines.Add("")

    $lines.Add("## Findings")
    $lines.Add("")
    $groups = $Report.findings | Group-Object category
    foreach ($group in $groups) {
        $lines.Add("### $($group.Name)")
        $lines.Add("")
        $ruleGroups = $group.Group | Group-Object id
        foreach ($ruleGroup in $ruleGroups) {
            $first = $ruleGroup.Group[0]
            $occurrenceTotal = ($ruleGroup.Group | Measure-Object -Property occurrenceCount -Sum).Sum
            $lines.Add("#### $($ruleGroup.Name) — $($first.title)")
            $lines.Add("")
            $lines.Add("- Severidade: $($first.severity)")
            $lines.Add("- Ocorrências: $occurrenceTotal")
            if ($first.recommendation) {
                $lines.Add("- Correção: $($first.recommendation)")
            }
            foreach ($finding in $ruleGroup.Group) {
                $location = if ($finding.file) {
                    "$($finding.file)" + $(if ($finding.line) { ":$($finding.line)" } else { "" })
                }
                else {
                    "repositório"
                }
                $suffix = if ($finding.occurrenceCount -gt 1) {
                    " ($($finding.occurrenceCount) ocorrências neste arquivo)"
                }
                else {
                    ""
                }
                $lines.Add("  - ``$location``: $($finding.message)$suffix")
            }
            $lines.Add("")
        }
    }

    $lines.Add("## Projetos")
    $lines.Add("")
    foreach ($project in $Report.projects) {
        $lines.Add("- `$($project.path)` — restore=$($project.restore.exitCode), build=$($project.build.exitCode), format=$($project.format.exitCode)")
    }
    $lines.Add("")
    $lines.Add("## Ferramentas")
    $lines.Add("")
    foreach ($tool in $Report.tools) {
        $version = if ($tool.version) { $tool.version } else { "indisponível" }
        $lines.Add("- $($tool.name): $version")
    }

    $lines | Set-Content -Path $Path -Encoding utf8
}

function Invoke-CodeQuality {
$script:Root = Resolve-AbsolutePath -Path $RepositoryRoot -BasePath (Get-Location).Path
$script:OutputRoot = Resolve-AbsolutePath -Path $OutputDirectory -BasePath $script:Root
$script:RawDirectory = Join-Path $script:OutputRoot "raw"
$script:LogDirectory = Join-Path $script:OutputRoot "logs"

New-Item -ItemType Directory -Path $script:OutputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $script:RawDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $script:LogDirectory -Force | Out-Null

$dotnetVersion = @(& $DotnetCommand --version 2>$null)
$script:Tools.Add([pscustomobject]@{
    name = "dotnet"
    version = ($dotnetVersion -join " ").Trim()
    available = $LASTEXITCODE -eq 0
})

$trackedFiles = Get-TrackedFiles
$analysisFiles = Get-AnalysisFiles -TrackedFiles $trackedFiles
$untrackedAnalysisFiles = @($analysisFiles | Where-Object { $trackedFiles -notcontains $_ })
Test-RepositoryHygiene -TrackedFiles $trackedFiles
Test-CustomSecrets -Files $trackedFiles -BlockingSecrets $true
Test-CustomSecrets -Files $untrackedAnalysisFiles -BlockingSecrets $false
Test-PlaintextPasswordPersistence -Files $analysisFiles
Test-CustomCodePractices -TrackedFiles $analysisFiles
Invoke-Gitleaks

$projectPaths = @(Get-ChildItem -Path (Join-Path $script:Root "sources") -Filter "*.csproj" -File -Recurse -ErrorAction SilentlyContinue |
    Sort-Object FullName |
    Select-Object -ExpandProperty FullName)

if ($projectPaths.Count -eq 0) {
    Add-Finding `
        -Id "FIAP0004" `
        -Source "fiap" `
        -Category "build" `
        -Severity "info" `
        -Title "Nenhum projeto encontrado" `
        -Message "Ainda não existem projetos .csproj em sources/." `
        -Recommendation "Adicione a atividade em sources/aula-NN quando iniciar o curso."
}

foreach ($projectPath in $projectPaths) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    $safeName = ($projectName -replace "[^A-Za-z0-9_.-]", "_")
    $frameworks = @(Get-TargetFrameworks $projectPath)
    Test-TargetFrameworkSupport -Frameworks $frameworks -Project $projectPath

    $restoreLog = Join-Path $script:LogDirectory "$safeName-restore.log"
    $restore = Invoke-CapturedCommand `
        -Command $DotnetCommand `
        -Arguments @("restore", $projectPath, "--nologo") `
        -LogPath $restoreLog
    Add-DotnetDiagnostics -Output $restore.output -Project $projectPath

    $buildLog = Join-Path $script:LogDirectory "$safeName-build.log"
    if ($restore.exitCode -eq 0) {
        $build = Invoke-CapturedCommand `
            -Command $DotnetCommand `
            -Arguments @(
                "build", $projectPath,
                "--no-restore",
                "--nologo",
                "--verbosity", "minimal",
                "-p:EnableNETAnalyzers=true",
                "-p:EnforceCodeStyleInBuild=true"
            ) `
            -LogPath $buildLog
        Add-DotnetDiagnostics -Output $build.output -Project $projectPath
    }
    else {
        $build = [pscustomobject]@{
            exitCode = -1
            output = @()
            logPath = Get-RelativePath $buildLog
        }
    }

    if ($restore.exitCode -ne 0 -or $build.exitCode -ne 0) {
        Add-Finding `
            -Id "FIAP-BUILD" `
            -Source "fiap" `
            -Category "build" `
            -Severity "error" `
            -Title "Projeto não compila" `
            -Message "Restore ou build falhou para $projectName." `
            -Project $projectPath `
            -Recommendation "Corrija os erros registrados no log de restore/build." `
            -Blocking $true
    }

    $formatReport = Join-Path $script:RawDirectory "$safeName-format.json"
    $formatLog = Join-Path $script:LogDirectory "$safeName-format.log"
    if ($restore.exitCode -eq 0) {
        $format = Invoke-CapturedCommand `
            -Command $DotnetCommand `
            -Arguments @(
                "format", $projectPath,
                "--verify-no-changes",
                "--no-restore",
                "--severity", "warn",
                "--report", $formatReport
            ) `
            -LogPath $formatLog
        Add-FormatDiagnostics -ReportPath $formatReport -Project $projectPath
    }
    else {
        $format = [pscustomobject]@{
            exitCode = -1
            output = @()
            logPath = Get-RelativePath $formatLog
        }
    }

    $script:Projects.Add([pscustomobject][ordered]@{
        path = Get-RelativePath $projectPath
        name = $projectName
        targetFrameworks = $frameworks
        restore = [pscustomobject]@{
            exitCode = $restore.exitCode
            log = $restore.logPath
        }
        build = [pscustomobject]@{
            exitCode = $build.exitCode
            log = $build.logPath
        }
        format = [pscustomobject]@{
            exitCode = $format.exitCode
            log = $format.logPath
            report = if (Test-Path $formatReport) { Get-RelativePath $formatReport } else { $null }
        }
    })
}

$testProjects = @($projectPaths | Where-Object {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($_)
    $content = Get-Content -Raw $_
    $name -match "(?i)(tests?|specs?)$" -or $content -match "<IsTestProject>\s*true\s*</IsTestProject>"
})

if ($testProjects.Count -eq 0) {
    Add-Finding `
        -Id "FIAP0005" `
        -Source "fiap" `
        -Category "best-practices" `
        -Severity "info" `
        -Title "Nenhum projeto de teste encontrado" `
        -Message "A ausência de testes não reduz a pontuação nesta fase." `
        -Recommendation "Adicione testes quando esse conteúdo fizer parte da disciplina."
}
else {
    foreach ($testProject in $testProjects) {
        $testName = [System.IO.Path]::GetFileNameWithoutExtension($testProject)
        $testLog = Join-Path $script:LogDirectory "$testName-test.log"
        $testResult = Invoke-CapturedCommand `
            -Command $DotnetCommand `
            -Arguments @("test", $testProject, "--no-restore", "--nologo") `
            -LogPath $testLog
        if ($testResult.exitCode -ne 0) {
            Add-Finding `
                -Id "FIAP4001" `
                -Source "test" `
                -Category "best-practices" `
                -Severity "warning" `
                -Title "Teste existente falhou" `
                -Message "O projeto de teste $testName terminou com falha." `
                -Project $testProject `
                -Recommendation "Corrija o código ou o teste e execute novamente."
        }
    }
}

$gitBranch = $null
$gitCommit = $null
try {
    $gitBranch = (& git -C $script:Root branch --show-current 2>$null).Trim()
    $gitCommit = (& git -C $script:Root rev-parse HEAD 2>$null).Trim()
}
catch {
    $gitBranch = $null
    $gitCommit = $null
}

Merge-Findings
$hasProjects = $script:Projects.Count -gt 0
$score = if ($hasProjects) { Get-Score } else { Get-UnscoredScore }
$blockingCount = @($script:Findings | Where-Object { $_.blocking }).Count
$occurrenceMeasure = $script:Findings | Measure-Object -Property occurrenceCount -Sum
$occurrenceTotal = if ($null -eq $occurrenceMeasure -or $null -eq $occurrenceMeasure.Sum) {
    0
}
else {
    [int]$occurrenceMeasure.Sum
}
$status = if ($blockingCount -gt 0) {
    "failed"
}
elseif (-not $hasProjects) {
    "not-scored"
}
else {
    "passed"
}
$exitCode = if ($blockingCount -gt 0) { 1 } else { 0 }

$report = [pscustomobject][ordered]@{
    schemaVersion = $script:SchemaVersion
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    repository = [pscustomobject][ordered]@{
        root = "."
        branch = $gitBranch
        commit = $gitCommit
    }
    status = $status
    exitCode = $exitCode
    score = $score
    summary = [pscustomobject][ordered]@{
        projects = $script:Projects.Count
        findings = $script:Findings.Count
        occurrences = $occurrenceTotal
        uniqueRules = @($script:Findings | Select-Object -ExpandProperty id -Unique).Count
        blocking = $blockingCount
        errors = @($script:Findings | Where-Object { $_.severity -eq "error" }).Count
        warnings = @($script:Findings | Where-Object { $_.severity -eq "warning" }).Count
        information = @($script:Findings | Where-Object { $_.severity -eq "info" }).Count
    }
    tools = @($script:Tools)
    projects = @($script:Projects)
    findings = @($script:Findings)
}

$jsonPath = Join-Path $script:OutputRoot "report.json"
$markdownPath = Join-Path $script:OutputRoot "report.md"
$report | ConvertTo-Json -Depth 20 | Set-Content -Path $jsonPath -Encoding utf8
Write-MarkdownReport -Report $report -Path $markdownPath
Write-GitHubAnnotations

if ($Ci -and $env:GITHUB_STEP_SUMMARY) {
    Get-Content $markdownPath | Add-Content -Path $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

Write-Output "Relatório JSON: $jsonPath"
Write-Output "Relatório Markdown: $markdownPath"
Write-Output $(if ($null -eq $score.final) { "Pontuação: não avaliada" } else { "Pontuação: $($score.final)/100" })
Write-Output "Status: $status"

if ($exitCode -ne 0) {
    Write-Error "A análise encontrou findings bloqueantes. Consulte os relatórios gerados." -ErrorAction Stop
}
}

if ($MyInvocation.InvocationName -ne ".") {
    Invoke-CodeQuality
}
