#requires -Version 7.4
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskResults = Join-Path $taskRepo 'TestResults/E12'
New-Item -ItemType Directory -Path $taskResults -Force | Out-Null
Push-Location $taskRepo
try {
    & dotnet build FCG.Users.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'A compilação falhou; os cenários E12 não foram iniciados.' }
    $taskFilter = 'FullyQualifiedName~FCG.Users.IntegrationTests.Messaging|FullyQualifiedName~TestesConfiguracaoOutbox'
    & dotnet test tests/FCG.Users.IntegrationTests -c Release --no-build --no-restore --filter $taskFilter --logger 'trx;LogFileName=e12-cenarios.trx' --results-directory $taskResults
    if ($LASTEXITCODE -ne 0) { throw 'Algum cenário E12 falhou; consulte o TRX local.' }

    [xml]$taskXml = Get-Content -LiteralPath (Join-Path $taskResults 'e12-cenarios.trx') -Raw
    $taskCounters = $taskXml.TestRun.ResultSummary.Counters
    $taskStart = [DateTimeOffset]::Parse([string]$taskXml.TestRun.Times.start,[Globalization.CultureInfo]::InvariantCulture)
    $taskFinish = [DateTimeOffset]::Parse([string]$taskXml.TestRun.Times.finish,[Globalization.CultureInfo]::InvariantCulture)
    $taskEvidence = [ordered]@{
        entrega = 'E12'
        resultado = 'aprovado'
        inicioUtc = $taskStart.ToUniversalTime().ToString('o',[Globalization.CultureInfo]::InvariantCulture)
        fimUtc = $taskFinish.ToUniversalTime().ToString('o',[Globalization.CultureInfo]::InvariantCulture)
        total = [int]$taskCounters.total
        aprovados = [int]$taskCounters.passed
        falhas = [int]$taskCounters.failed
        naoExecutados = [int]$taskCounters.notExecuted
        postgresql = '16-alpine, bancos descartáveis'
        rabbitmqImagem = 'rabbitmq@sha256:8dd6e3570ddaa2ef82a6c3a8950e79c1f73893adfbdddbc374fc1f1ac8a0f5dd'
        masstransit = '8.3.0'
        http = 'Host Program em TestServer; transporte AMQP e PostgreSQL reais nos cenários integrados'
        cenarios = @('configuracao','confirm-e-mandatory','fila-duravel','processamento-e-baixa',
            'falhas-sem-perda','cancelamento','reserva-skip-locked','cadastro-com-broker-inacessivel','recuperacao-de-binding')
        observacao = 'Processador usa PostgreSQL real e transporte substituto em seus testes de falhas; host e transporte usam RabbitMQ real nos cenários correspondentes. Nenhum consumidor Notifications ou envio de email foi executado.'
    }
    $taskEvidence | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskResults 'publicador-e12.json') -Encoding utf8
    Write-Host 'Provas E12 aprovadas. Resumo sem credenciais: TestResults/E12/publicador-e12.json.'
}
finally { Pop-Location }
