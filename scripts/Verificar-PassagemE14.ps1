#requires -Version 7.4
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskResults = Join-Path $taskRepo 'TestResults/E14'
$taskSource = Join-Path $taskResults ('fonte-' + [guid]::NewGuid().ToString('N'))
$taskPublish = Join-Path $taskSource 'TestResults/E14/api-publicada'
$taskOldArtifact = [Environment]::GetEnvironmentVariable('FCG_E14_PUBLISHED_API')
$taskOldConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__UsersDatabase')
$taskStarted = [DateTimeOffset]::UtcNow
New-Item -ItemType Directory -Path $taskSource -Force | Out-Null
Push-Location $taskRepo
try {
    $taskFiles = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível inventariar a fonte Git atual.' }
    $taskManifest = [Collections.Generic.List[object]]::new()
    foreach ($taskRelative in $taskFiles) {
        # Copia fontes atuais, inclusive alterações ainda não commitadas. Nunca copia segredos ou saídas locais.
        if ($taskRelative -match '(^|/)(\.git|\.codex|\.agents|\.aws|\.local|secrets|bin|obj|TestResults|coverage)(/|$)' -or
            $taskRelative -match '(^|/)\.env($|\.)|\.(pem|key|pfx|p12|log|tmp)$|(^|/)(secrets\.json|appsettings\..*Local\.json)$') { continue }
        $taskOriginal = [IO.Path]::GetFullPath((Join-Path $taskRepo $taskRelative))
        $taskDestination = [IO.Path]::GetFullPath((Join-Path $taskSource $taskRelative))
        if (-not $taskOriginal.StartsWith($taskRepo + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
            -not $taskDestination.StartsWith($taskSource + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Caminho de fonte fora da área esperada.' }
        if (-not (Test-Path -LiteralPath $taskOriginal -PathType Leaf)) { continue } # arquivo excluído na working tree
        if ((Get-Item -LiteralPath $taskOriginal).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'A cópia E14 não segue links de arquivos.' }
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
        Copy-Item -LiteralPath $taskOriginal -Destination $taskDestination
        $taskManifest.Add([ordered]@{ arquivo = $taskRelative; sha256 = (Get-FileHash -LiteralPath $taskDestination -Algorithm SHA256).Hash })
    }
    if (-not (Test-Path -LiteralPath (Join-Path $taskSource 'FCG.Users.sln'))) { throw 'A cópia de fonte está incompleta.' }
    $taskManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskResults 'manifesto-fonte-e14.json') -Encoding utf8
    Write-Host 'Fonte copiada sem bin/obj, segredos, bancos ou chaves pessoais. Iniciando validação isolada.'
    Push-Location $taskSource
    try {
        & dotnet restore FCG.Users.sln --nologo
        if ($LASTEXITCODE -ne 0) { throw 'A restauração da cópia limpa falhou.' }
        & dotnet build FCG.Users.sln -c Release --no-restore --nologo
        if ($LASTEXITCODE -ne 0) { throw 'A compilação limpa falhou.' }
        & dotnet publish src/FCG.Users.Api -c Release --no-build --no-restore --nologo -o $taskPublish
        if ($LASTEXITCODE -ne 0) { throw 'A publicação da API falhou.' }
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'Não foi possível restaurar a ferramenta EF declarada pelo repositório.' }
        # A factory de design não conecta ao banco; string sintética apenas para construir o modelo.
        [Environment]::SetEnvironmentVariable('ConnectionStrings__UsersDatabase','Host=127.0.0.1;Port=1;Database=e14_modelo;Username=e14_sintetico;Password=e14_sintetico')
        try {
            & dotnet ef migrations script --idempotent --project src/FCG.Users.Infrastructure --startup-project src/FCG.Users.Infrastructure --context UsersDbContext --configuration Release --no-build --output (Join-Path $taskResults 'migrations-users-e14.sql')
            if ($LASTEXITCODE -ne 0) { throw 'A geração do SQL completo falhou.' }
        }
        finally { [Environment]::SetEnvironmentVariable('ConnectionStrings__UsersDatabase',$taskOldConnection) }
        [Environment]::SetEnvironmentVariable('FCG_E14_PUBLISHED_API',$taskPublish)
        & dotnet test tests/FCG.Users.UnitTests -c Release --no-build --no-restore --logger 'trx;LogFileName=e14-unitarios.trx' --results-directory $taskResults
        if ($LASTEXITCODE -ne 0) { throw 'Testes unitários falharam na cópia limpa.' }
        & dotnet test tests/FCG.Users.IntegrationTests -c Release --no-build --no-restore --logger 'trx;LogFileName=e14-integracao.trx' --results-directory $taskResults
        if ($LASTEXITCODE -ne 0) { throw 'Testes de integração falharam na cópia limpa.' }
    }
    finally { Pop-Location }
    $taskSuites = [Collections.Generic.List[object]]::new()
    $taskEvidence = @()
    foreach ($taskSuite in @('unitarios','integracao')) {
        [xml]$taskXml = Get-Content -LiteralPath (Join-Path $taskResults ('e14-' + $taskSuite + '.trx')) -Raw
        $taskCounters = $taskXml.TestRun.ResultSummary.Counters
        if ([int]$taskCounters.total -eq 0 -or [int]$taskCounters.passed -ne [int]$taskCounters.total) { throw 'A suíte não aprovou todos os casos descobertos.' }
        $taskSuites.Add([ordered]@{ suite = $taskSuite; total = [int]$taskCounters.total; aprovados = [int]$taskCounters.passed; falhas = [int]$taskCounters.failed; naoExecutados = [int]$taskCounters.notExecuted })
        foreach ($taskResult in $taskXml.TestRun.Results.UnitTestResult) {
            foreach ($taskLine in ([string]$taskResult.Output.StdOut -split '\r?\n')) {
                if (-not $taskLine.StartsWith('E14-EVIDENCIA:',[StringComparison]::Ordinal)) { continue }
                $taskParameters = @{ InputObject = $taskLine.Substring('E14-EVIDENCIA:'.Length); AsHashtable = $true }
                if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $taskParameters.DateKind = 'String' }
                $taskEvidence += ConvertFrom-Json @taskParameters
            }
        }
    }
    if ($taskEvidence.Count -ne 1) { throw 'A prova com artefato publicado não produziu a evidência esperada.' }
    $taskSummary = [ordered]@{
        entrega = 'E14'; resultado = 'aprovado'
        inicioUtc = $taskStarted.ToString('o'); fimUtc = [DateTimeOffset]::UtcNow.ToString('o')
        fonte = 'Cópia da working tree atual, sem bin/obj/.git/segredos; não é clone de um commit publicado.'
        arquivosFonte = $taskManifest.Count
        compilacao = 'Release; warnings tratados como erros'
        sdk = (& dotnet --version | Out-String).Trim()
        suites = @($taskSuites)
        total = [int](($taskSuites | ForEach-Object { $_['aprovados'] } | Measure-Object -Sum).Sum)
        passagem = $taskEvidence[0]
        sqlIdempotenteSha256 = (Get-FileHash -LiteralPath (Join-Path $taskResults 'migrations-users-e14.sql') -Algorithm SHA256).Hash
        observacao = 'Recursos novos por Testcontainers e chaves temporárias. Sem banco do desenvolvedor, monólito, execução de Catalog/Notifications, email ou implantação.'
    }
    $taskSummary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $taskResults 'passagem-e14.json') -Encoding utf8
    Write-Host 'Passagem E14 e suíte completa aprovadas. Resumo: TestResults/E14/passagem-e14.json.'
}
finally {
    [Environment]::SetEnvironmentVariable('FCG_E14_PUBLISHED_API',$taskOldArtifact)
    [Environment]::SetEnvironmentVariable('ConnectionStrings__UsersDatabase',$taskOldConnection)
    Pop-Location
}
