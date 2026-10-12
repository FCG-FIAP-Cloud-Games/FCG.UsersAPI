#requires -Version 7.4
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskResults = Join-Path $taskRepo 'TestResults/E13'
New-Item -ItemType Directory -Path $taskResults -Force | Out-Null
Push-Location $taskRepo
try {
    & dotnet build FCG.Users.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou; as provas E13 não foram iniciadas.' }
    & dotnet test tests/FCG.Users.IntegrationTests -c Release --no-build --no-restore --filter 'FullyQualifiedName~TestesRecuperacaoOutboxE13' --logger 'trx;LogFileName=e13-cenarios.trx' --results-directory $taskResults
    if ($LASTEXITCODE -ne 0) { throw 'Alguma prova E13 falhou; consulte o TRX local.' }

    [xml]$taskXml = Get-Content -LiteralPath (Join-Path $taskResults 'e13-cenarios.trx') -Raw
    $taskCounters = $taskXml.TestRun.ResultSummary.Counters
    if ([int]$taskCounters.total -ne 5 -or [int]$taskCounters.passed -ne 5) { throw 'O runner precisa executar e aprovar os cinco cenários E13.' }
    $taskStart = [DateTimeOffset]::Parse([string]$taskXml.TestRun.Times.start,[Globalization.CultureInfo]::InvariantCulture)
    $taskFinish = [DateTimeOffset]::Parse([string]$taskXml.TestRun.Times.finish,[Globalization.CultureInfo]::InvariantCulture)
    $taskCases = [Collections.Generic.List[object]]::new()
    foreach ($taskResult in $taskXml.TestRun.Results.UnitTestResult) {
        foreach ($taskLine in ([string]$taskResult.Output.StdOut -split '\r?\n')) {
            if (-not $taskLine.StartsWith('E13-EVIDENCIA:',[StringComparison]::Ordinal)) { continue }
            $taskParameters = @{ InputObject = $taskLine.Substring('E13-EVIDENCIA:'.Length); AsHashtable = $true }
            # PowerShell 7.5 pode converter datas automaticamente; preservar as strings UTC da prova.
            if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $taskParameters.DateKind = 'String' }
            $taskCase = ConvertFrom-Json @taskParameters
            if (-not $taskCase.cenario -or -not $taskCase.dados) { throw 'A evidência de um cenário E13 está incompleta.' }
            $taskCases.Add($taskCase)
        }
    }
    $taskExpected = @('broker-retorna','reinicio-com-pendencia','crash-apos-envio','duas-instancias','cadastro-revertido') | Sort-Object
    $taskActual = @($taskCases | ForEach-Object { $_.cenario } | Sort-Object)
    if ($taskCases.Count -ne 5 -or (Compare-Object $taskExpected $taskActual)) { throw 'As cinco evidências por cenário não foram encontradas no TRX.' }
    $taskEvidence = [ordered]@{
        entrega = 'E13'
        resultado = 'aprovado'
        inicioUtc = $taskStart.ToUniversalTime().ToString('o',[Globalization.CultureInfo]::InvariantCulture)
        fimUtc = $taskFinish.ToUniversalTime().ToString('o',[Globalization.CultureInfo]::InvariantCulture)
        total = [int]$taskCounters.total
        aprovados = [int]$taskCounters.passed
        falhas = [int]$taskCounters.failed
        naoExecutados = [int]$taskCounters.notExecuted
        http = 'Processos reais dotnet/Program em Kestrel, HTTP por socket loopback e portas temporárias'
        postgresql = '16-alpine; bancos descartáveis com migrations reais'
        rabbitmqImagem = 'rabbitmq@sha256:8dd6e3570ddaa2ef82a6c3a8950e79c1f73893adfbdddbc374fc1f1ac8a0f5dd'
        masstransit = '8.3.0'
        cenarios = @($taskCases | Sort-Object { $_.cenario })
        observacao = 'API/publicador/transporte reais. Sem consumidor Notifications ou email. A barreira de banco existe só no teste; nenhuma opção ou endpoint de falha foi adicionado à API.'
    }
    $taskEvidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $taskResults 'recuperacao-e13.json') -Encoding utf8
    Write-Host 'Provas E13 aprovadas. Estados e IDs sintéticos: TestResults/E13/recuperacao-e13.json.'
}
finally { Pop-Location }
