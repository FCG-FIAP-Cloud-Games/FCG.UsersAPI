#requires -Version 7.4
[CmdletBinding()]
param([ValidateRange(1024,65535)][int]$Porta = 5091)

$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Configurar-AmbienteE11Local.ps1')
$taskDll = Join-Path $taskRepo 'src/FCG.Users.Api/bin/Release/net8.0/FCG.Users.Api.dll'
if (-not (Test-Path -LiteralPath $taskDll)) { throw 'Compile a solução em Release antes desta demonstração.' }
if (Get-NetTCPConnection -State Listen -LocalPort $Porta -ErrorAction SilentlyContinue) { throw 'A porta escolhida já está ocupada.' }
$taskResults = Join-Path $taskRepo 'TestResults/E11'
New-Item -ItemType Directory -Path $taskResults -Force | Out-Null
$taskStart = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet).Source)
$taskStart.UseShellExecute = $false
$taskStart.CreateNoWindow = $true
$taskStart.RedirectStandardOutput = $true
$taskStart.RedirectStandardError = $true
$taskStart.WorkingDirectory = Join-Path $taskRepo 'src/FCG.Users.Api'
$taskStart.ArgumentList.Add($taskDll)
$taskStart.Environment['DOTNET_ENVIRONMENT'] = 'Development'
$taskStart.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
$taskStart.Environment['ASPNETCORE_URLS'] = "http://127.0.0.1:$Porta"
# Esta demonstração conserva o comportamento E11, mesmo após o publicador E12 existir.
$taskStart.Environment['Outbox__Enabled'] = 'false'
$taskStart.Environment['RabbitMq__Host'] = '127.0.0.1'
$taskStart.Environment['RabbitMq__Port'] = '1'
$taskProcess = [Diagnostics.Process]::new()
$taskProcess.StartInfo = $taskStart
$taskStarted = $false
$taskStdout = $null
$taskStderr = $null
try {
    if (-not $taskProcess.Start()) { throw 'Falha ao iniciar a API da demonstração.' }
    $taskStarted = $true
    $taskStdout = $taskProcess.StandardOutput.ReadToEndAsync()
    $taskStderr = $taskProcess.StandardError.ReadToEndAsync()
    $taskBase = "http://127.0.0.1:$Porta"
    $taskReady = $false
    for ($taskAttempt = 0; $taskAttempt -lt 60; $taskAttempt++) {
        if ($taskProcess.HasExited) { throw 'A API encerrou antes de ficar pronta; consulte os logs locais em TestResults/E11.' }
        try {
            $taskHealth = Invoke-WebRequest -Uri "$taskBase/health" -TimeoutSec 1
            if ($taskHealth.StatusCode -eq 200) { $taskReady = $true; break }
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $taskReady) { throw 'A API não respondeu dentro do limite da demonstração.' }
    $taskCorrelation = [guid]::NewGuid()
    $taskBody = @{
        nome = '  Pessoa   Sintética E11 '
        cpf = [Random]::Shared.NextInt64(10000000000,100000000000).ToString([Globalization.CultureInfo]::InvariantCulture)
        email = "e11-local-$([guid]::NewGuid().ToString('N'))@exemplo.test"
        dataNascimento = '2000-01-01'
        senha = 'SenhaSintetica#E11'
    } | ConvertTo-Json
    $taskResponse = Invoke-WebRequest -Method Post -Uri "$taskBase/api/v1/usuarios" -ContentType 'application/json' -Body $taskBody -Headers @{ 'X-Correlation-ID' = $taskCorrelation.ToString('D') }
    if ($taskResponse.StatusCode -ne 201) { throw 'O cadastro não retornou 201.' }
    $taskUserId = [guid](($taskResponse.Content | ConvertFrom-Json).id)
    $taskHeader = @($taskResponse.Headers['X-Correlation-ID'])[0]
    if ([guid]$taskHeader -ne $taskCorrelation) { throw 'O cabeçalho de correlação não foi preservado.' }
    $taskDatabase = @{}
    foreach ($taskLine in Get-Content -LiteralPath (Join-Path $taskRepo '.env.usersdb')) {
        if ($taskLine -match '^(POSTGRES_USER|POSTGRES_DB)=(.*)$') { $taskDatabase[$Matches[1]] = $Matches[2] }
    }
    # As únicas interpolações no SQL são GUIDs já tipados e gerados nesta execução.
    $taskSql = @"
SELECT json_build_object(
'usuarios', (SELECT count(*) FROM "tb_Usuarios" WHERE "Id" = '$($taskUserId.ToString('D'))'::uuid),
'auditorias', (SELECT count(*) FROM "tb_LogUsuarios" WHERE "UsuarioId" = '$($taskUserId.ToString('D'))'::uuid),
'eventos', (SELECT count(*) FROM "tb_Outbox" WHERE "UserId" = '$($taskUserId.ToString('D'))'::uuid),
'pendente', o."PublishedAt" IS NULL,
'attempts', o."Attempts",
'correlacaoPreservada', o."CorrelationId" = '$($taskCorrelation.ToString('D'))'::uuid,
'snapshotCorreto', o."Payload"->>'name' = u."Nome" AND o."Payload"->>'email' = u."Email",
'camposPayload', (SELECT count(*) FROM jsonb_object_keys(o."Payload")),
'semCamposSecretos', NOT (o."Payload" ?| ARRAY['cpf','senha','senhaHash','refreshToken','accessToken'])
) FROM "tb_Outbox" o JOIN "tb_Usuarios" u ON u."Id" = o."UserId"
WHERE o."UserId" = '$($taskUserId.ToString('D'))'::uuid;
"@
    $taskQuery = $taskSql | & docker exec -i fcg-usersapi-postgres psql -v ON_ERROR_STOP=1 -U $taskDatabase['POSTGRES_USER'] -d $taskDatabase['POSTGRES_DB'] -At
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($taskQuery)) { throw 'Falha ao consultar o resultado persistido.' }
    $taskState = $taskQuery | ConvertFrom-Json
    if ($taskState.usuarios -ne 1 -or $taskState.auditorias -ne 1 -or $taskState.eventos -ne 1 -or -not $taskState.pendente -or $taskState.attempts -ne 0 -or -not $taskState.correlacaoPreservada -or -not $taskState.snapshotCorreto -or $taskState.camposPayload -ne 7 -or -not $taskState.semCamposSecretos) {
        throw 'A persistência não corresponde aos critérios da E11.'
    }
    $taskEvidence = [ordered]@{
        entrega = 'E11'
        instanteUtc = [DateTimeOffset]::UtcNow.ToString('o')
        http = [int]$taskResponse.StatusCode
        userId = $taskUserId
        correlationId = $taskCorrelation
        banco = $taskState
        transporteRegistrado = $false
        rabbitConfigurado = '127.0.0.1:1 (inacessível)'
        observacao = 'Cadastro público via Kestrel e banco local; rollback e demais caminhos estão nos testes de integração.'
    }
    $taskEvidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $taskResults 'http-outbox-local.json') -Encoding utf8
    Write-Host 'E11 validada: HTTP 201; 1 usuário, 1 auditoria, 1 evento pendente; 0 tentativas. Relatório em TestResults/E11/http-outbox-local.json.'
    Write-Host 'A conta sintética e seu evento permanecem no banco local. Reexecutar cria outra conta sintética.'
}
finally {
    if ($taskStarted -and -not $taskProcess.HasExited) { $taskProcess.Kill($true); $taskProcess.WaitForExit() }
    if ($taskStdout) { $taskStdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $taskResults 'kestrel-outbox-stdout.log') -Encoding utf8 }
    if ($taskStderr) { $taskStderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $taskResults 'kestrel-outbox-stderr.log') -Encoding utf8 }
    $taskProcess.Dispose()
}
