#requires -Version 7.4
# Nome histórico preservado: padrão E16; -SemArquivosRabbitMq prova E17 com broker novo sem JSON/conf montados.
[CmdletBinding()]
param([switch]$SemArquivosRabbitMq)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$taskRepo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskEntrega = if ($SemArquivosRabbitMq) { 'E17' } else { 'E16' }
$taskEntregaTag = $taskEntrega.ToLowerInvariant()
$taskResults = Join-Path $taskRepo ('TestResults/' + $taskEntrega)
$taskId = [guid]::NewGuid().ToString('N')
$taskSource = Join-Path $taskResults ('fonte-' + $taskId)
$taskProject = 'fcg-users-' + $taskEntregaTag + '-' + $taskId
$taskComposeFile = Join-Path $taskSource 'compose.yaml'
$taskComposeArguments = @('compose', '--project-name', $taskProject, '--file', $taskComposeFile)
$taskStarted = [DateTimeOffset]::UtcNow
$taskOldHttpPort = [Environment]::GetEnvironmentVariable('FCG_USERS_HTTP_PORT')
$taskOldRabbitPort = [Environment]::GetEnvironmentVariable('FCG_RABBIT_MANAGEMENT_PORT')
$taskManifest = [Collections.Generic.List[object]]::new()
$taskChecks = [Collections.Generic.List[string]]::new()
$taskGuardContainers = [Collections.Generic.List[string]]::new()
$taskGuardResults = [Collections.Generic.List[object]]::new()
$taskGuardComposeArguments = $null
$taskOwnProject = $false
$taskFailure = $null
$taskSummary = $null
$taskStage = 'inventário da fonte'
$taskUrn = 'urn:message:FCG.Notifications.Application.Messaging.Contracts:UserCreatedEvent'

function Assert-E16 {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw [InvalidOperationException]::new($taskEntrega + ': ' + $Message) }
}

function ConvertFrom-JsonE16 {
    param([string]$Json)
    $taskParameters = @{ InputObject = $Json; AsHashtable = $true }
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $taskParameters.DateKind = 'String' }
    return ConvertFrom-Json @taskParameters
}

function Invoke-DockerCaptureE16 {
    param([string[]]$Arguments)
    $taskOutput = @(& docker @Arguments 2>&1)
    $taskExit = $LASTEXITCODE
    Assert-E16 ($taskExit -eq 0) ('Uma consulta Docker falhou, código ' + $taskExit + '.')
    return ($taskOutput | Out-String).Trim()
}

function Invoke-ComposeCaptureE16 {
    param([string[]]$Arguments)
    return Invoke-DockerCaptureE16 -Arguments ($taskComposeArguments + $Arguments)
}

function Invoke-ComposeVisibleE16 {
    param([string[]]$Arguments)
    & docker @taskComposeArguments @Arguments
    Assert-E16 ($LASTEXITCODE -eq 0) 'O comando Compose da demonstração falhou.'
}

function Get-FreePortE16 {
    $taskListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $taskListener.Start()
        return ([Net.IPEndPoint]$taskListener.LocalEndpoint).Port
    }
    finally { $taskListener.Stop() }
}

function Get-ServiceIdE16 {
    param([string]$Service)
    $taskContainer = Invoke-ComposeCaptureE16 -Arguments @('ps', '--all', '--quiet', $Service)
    Assert-E16 ($taskContainer -match '^[a-f0-9]{12,64}$') ('O serviço ' + $Service + ' não possui exatamente um contêiner da prova.')
    return $taskContainer
}

function Get-ServiceStateE16 {
    param([string]$Service)
    $taskContainer = Get-ServiceIdE16 -Service $Service
    return ConvertFrom-JsonE16 (Invoke-DockerCaptureE16 -Arguments @('inspect', '--format', '{{json .State}}', $taskContainer))
}

function Invoke-HttpE16 {
    param([string]$Uri, [string]$Method = 'GET', [hashtable]$Headers = @{}, [object]$Body = $null)
    $taskParameters = @{ Uri = $Uri; Method = $Method; Headers = $Headers; SkipHttpErrorCheck = $true; TimeoutSec = 10 }
    if ($null -ne $Body) {
        $taskParameters.Body = $Body | ConvertTo-Json -Depth 8 -Compress
        $taskParameters.ContentType = 'application/json; charset=utf-8'
    }
    try { return Invoke-WebRequest @taskParameters }
    catch { throw [InvalidOperationException]::new($taskEntrega + ': Uma chamada HTTP da prova não foi concluída.') }
}

function Wait-DependenciesE16 {
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(180)
    do {
        $taskPostgresState = Get-ServiceStateE16 -Service 'postgres'
        $taskRabbitState = Get-ServiceStateE16 -Service 'rabbitmq'
        Assert-E16 ($taskPostgresState.Status -notin @('dead', 'exited') -and $taskRabbitState.Status -notin @('dead', 'exited')) 'Uma dependência encerrou durante a inicialização.'
        if ($taskPostgresState.Health.Status -eq 'healthy' -and $taskRabbitState.Health.Status -eq 'healthy') { return }
        Start-Sleep -Milliseconds 750
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw [InvalidOperationException]::new($taskEntrega + ': As dependências não ficaram prontas dentro do prazo.')
}

function Wait-ReadyE16 {
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(180)
    do {
        $taskApiState = Get-ServiceStateE16 -Service 'usersapi'
        Assert-E16 ($taskApiState.Status -notin @('dead', 'exited')) 'A API encerrou durante a inicialização.'
        if ($taskApiState.Health.Status -eq 'healthy') {
            $taskHealth = Invoke-HttpE16 -Uri ($taskApiUri + '/health')
            if ([int]$taskHealth.StatusCode -eq 200) {
                $taskSwagger = Invoke-HttpE16 -Uri ($taskApiUri + '/swagger/index.html')
                Assert-E16 ([int]$taskSwagger.StatusCode -eq 200) 'O Swagger da demonstração não está disponível.'
                return
            }
        }
        Start-Sleep -Milliseconds 750
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw [InvalidOperationException]::new($taskEntrega + ': A demonstração não ficou pronta dentro do prazo.')
}

function Start-GuardContainerE16 {
    param([string[]]$Environment, [string]$Service = 'usersapi', [string[]]$Command = @())
    $taskName = $taskProject + '-guard-' + [guid]::NewGuid().ToString('N')
    Assert-E16 ($taskName -match '^fcg-users-e1[67]-[a-f0-9]{32}-guard-[a-f0-9]{32}$') 'Um nome de contêiner de guarda ficou fora do padrão da prova.'
    # Registra o nome antes do comando para a limpeza poder recuperar uma criação parcial.
    $taskGuardContainers.Add($taskName)
    $taskArguments = @('run', '--detach', '--no-deps', '--name', $taskName)
    foreach ($taskVariable in $Environment) { $taskArguments += @('--env', $taskVariable) }
    $taskArguments += @($Service) + $Command
    [void](Invoke-DockerCaptureE16 -Arguments ($taskGuardComposeArguments + $taskArguments))
    $taskIdGuard = Invoke-DockerCaptureE16 -Arguments @('inspect', '--format', '{{.Id}}', $taskName)
    Assert-E16 ($taskIdGuard -match '^[a-f0-9]{64}$') 'O contêiner de guarda não retornou um identificador válido.'
    return $taskName
}

function Get-GuardStateE16 {
    param([string]$Name)
    return ConvertFrom-JsonE16 (Invoke-DockerCaptureE16 -Arguments @('inspect', '--format', '{{json .State}}', $Name))
}

function Wait-GuardExitE16 {
    param([string]$Name, [int]$ExpectedExit = -1)
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        $taskState = Get-GuardStateE16 $Name
        if ($taskState.Status -in @('dead', 'exited')) {
            if ($ExpectedExit -ge 0) {
                Assert-E16 ([int]$taskState.ExitCode -eq $ExpectedExit) ('Uma execução de guarda não retornou exit ' + $ExpectedExit + '.')
            }
            else { Assert-E16 ([int]$taskState.ExitCode -ne 0) 'Uma inicialização sem os requisitos esperados retornou sucesso.' }
            return [int]$taskState.ExitCode
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw [InvalidOperationException]::new($taskEntrega + ': Um contêiner de guarda não encerrou no prazo; a limpeza removerá apenas os recursos da prova.')
}

function Remove-GuardContainerE16 {
    param([string]$Name)
    Assert-E16 ($taskGuardContainers.Contains($Name) -and $Name -match '^fcg-users-e1[67]-[a-f0-9]{32}-guard-[a-f0-9]{32}$') 'A limpeza de guarda recebeu um recurso não registrado.'
    $taskLabelOutput = @(& docker inspect --format '{{ index .Config.Labels "com.docker.compose.project" }}' $Name 2>&1)
    if ($LASTEXITCODE -ne 0) {
        # Uma criação pode ter falhado antes de existir um contêiner; aceita somente ausência confirmada.
        $taskAbsent = ($taskLabelOutput | Out-String)
        Assert-E16 ($taskAbsent -match 'No such (object|container)') 'Não foi possível confirmar a ausência do contêiner de guarda.'
    }
    else {
        Assert-E16 (($taskLabelOutput | Out-String).Trim() -ceq $taskProject) 'O contêiner de guarda não pertence ao projeto descartável.'
        [void](Invoke-DockerCaptureE16 -Arguments @('rm', '--force', $Name))
    }
    [void]$taskGuardContainers.Remove($Name)
}

function Assert-NoPreparationE16 {
    # Somente a prova usa este banco novo: não há tabelas antes da inicialização integrada.
    $taskTables = Invoke-ComposeCaptureE16 -Arguments @('exec', '-T', 'postgres', 'psql', '-U', 'fcg_users_demo', '-d', 'fcg_users_demo', '-At', '-c', "SELECT count(*) FROM pg_tables WHERE schemaname='public';")
    Assert-E16 ($taskTables -ceq '0') 'Uma execução recusada ou desabilitada criou tabelas no banco novo.'
    [void](Invoke-DockerCaptureE16 -Arguments ($taskGuardComposeArguments + @('run', '--rm', '--no-deps', '--entrypoint', '/bin/sh', 'usersapi', '-c', 'test ! -e /data/jwt/private.pem && test ! -e /data/jwt/public.pem')))
}

function Wait-GuardHealthyE16 {
    param([string]$Name)
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        $taskState = Get-GuardStateE16 $Name
        Assert-E16 ($taskState.Status -notin @('dead', 'exited')) 'A API com bootstrap desabilitado encerrou apesar do ambiente preparado.'
        if ($taskState.Health.Status -eq 'healthy') { return }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw [InvalidOperationException]::new($taskEntrega + ': A API com bootstrap desabilitado não ficou saudável no prazo.')
}

function Invoke-CancellationGuardE16 {
    $taskLockApplication = $taskEntregaTag + '-lock-' + $taskId
    $taskLockHolder = $null
    $taskCancelledApi = $null
    try {
        # Contêiner cliente temporário da imagem PostgreSQL já usada pela prova.
        # psql não inicia outro servidor nem altera o diretório de dados montado.
        $taskLockHolder = Start-GuardContainerE16 -Service 'postgres' -Environment @('PGPASSWORD=UsersDb_Demo_2026!', ('PGAPPNAME=' + $taskLockApplication)) -Command @(
            'psql', '-h', 'postgres', '-U', 'fcg_users_demo', '-d', 'fcg_users_demo', '-v', 'ON_ERROR_STOP=1', '-At', '-c',
            'SELECT pg_advisory_lock(84645159015001); SELECT pg_sleep(45);')
        $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
        do {
            $taskHeld = Invoke-ComposeCaptureE16 -Arguments @('exec', '-T', 'postgres', 'psql', '-U', 'fcg_users_demo', '-d', 'fcg_users_demo', '-At', '-c',
                ("SELECT count(*) FROM pg_locks l JOIN pg_stat_activity a ON a.pid=l.pid WHERE l.locktype='advisory' AND l.granted AND a.application_name='" + $taskLockApplication + "';"))
            if ($taskHeld -ceq '1') { break }
            Start-Sleep -Milliseconds 250
        } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
        Assert-E16 ($taskHeld -ceq '1') 'O cliente exclusivo da prova não obteve a trava da preparação.'

        $taskCancelledApi = Start-GuardContainerE16 -Environment @('DemonstracaoLocal__Habilitada=true', 'Outbox__Enabled=false')
        $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
        do {
            $taskWaiting = Invoke-ComposeCaptureE16 -Arguments @('exec', '-T', 'postgres', 'psql', '-U', 'fcg_users_demo', '-d', 'fcg_users_demo', '-At', '-c',
                "SELECT count(*) FROM pg_locks l JOIN pg_stat_activity a ON a.pid=l.pid WHERE l.locktype='advisory' AND NOT l.granted AND a.datname='fcg_users_demo';")
            if ($taskWaiting -ceq '1') { break }
            Assert-E16 ((Get-GuardStateE16 $taskCancelledApi).Status -eq 'running') 'O bootstrap encerrou antes do teste de cancelamento.'
            Start-Sleep -Milliseconds 250
        } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
        Assert-E16 ($taskWaiting -ceq '1') 'A preparação não chegou à espera pela trava para o teste de cancelamento.'
        [void](Invoke-DockerCaptureE16 -Arguments @('stop', '--time', '10', $taskCancelledApi))
        $taskCancelExit = Wait-GuardExitE16 -Name $taskCancelledApi -ExpectedExit 143
        $taskCancelledLogs = Invoke-DockerCaptureE16 -Arguments @('logs', $taskCancelledApi)
        Assert-E16 ($taskCancelledLogs -notmatch 'Preparação da demonstração concluída\.|Now listening on:|Application started\.') 'O cancelamento permitiu iniciar a API ou concluir a preparação.'
        Assert-NoPreparationE16
        $taskGuardResults.Add([ordered]@{ caso = 'SIGTERM durante espera pelo lock de preparação'; exit = $taskCancelExit; bootstrapConcluido = $false; apiIniciada = $false; tabelasOuChavesCriadas = $false })
    }
    finally {
        if ($null -ne $taskCancelledApi -and $taskGuardContainers.Contains($taskCancelledApi)) { Remove-GuardContainerE16 $taskCancelledApi }
        if ($null -ne $taskLockHolder -and $taskGuardContainers.Contains($taskLockHolder)) {
            # Termina somente a sessão psql nomeada pelo UUID desta prova e desta conta/banco.
            Assert-E16 ($taskLockApplication -match '^e1[67]-lock-[a-f0-9]{32}$') 'A sessão de lock não possui identidade exclusiva da prova.'
            [void](Invoke-ComposeCaptureE16 -Arguments @('exec', '-T', 'postgres', 'psql', '-U', 'fcg_users_demo', '-d', 'fcg_users_demo', '-At', '-c',
                ("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name='" + $taskLockApplication + "' AND datname='fcg_users_demo' AND usename='fcg_users_demo' AND backend_type='client backend' AND pid<>pg_backend_pid();")))
            Remove-GuardContainerE16 $taskLockHolder
        }
    }
}

function Get-DatabaseSnapshotE16 {
    $taskSql = @"
SELECT jsonb_build_object(
    'migrations', (SELECT count(*) FROM "__EFMigrationsHistory"),
    'users', (SELECT count(*) FROM "tb_Usuarios"),
    'admins', (SELECT count(*) FROM "tb_Usuarios" WHERE "PerfilId" = '22222222-2222-2222-2222-222222222222'::uuid),
    'logs', (SELECT count(*) FROM "tb_LogUsuarios"),
    'outbox', (SELECT count(*) FROM "tb_Outbox"),
    'published', (SELECT count(*) FROM "tb_Outbox" WHERE "PublishedAt" IS NOT NULL),
    'events', (SELECT coalesce(jsonb_agg(jsonb_build_object(
        'id', "Id", 'eventId', "EventId", 'correlationId', "CorrelationId", 'userId', "UserId",
        'eventType', "EventType", 'payload', "Payload", 'attempts', "Attempts") ORDER BY "EventId"), '[]'::jsonb) FROM "tb_Outbox"),
    'accounts', (SELECT coalesce(jsonb_agg(jsonb_build_object(
        'id', "Id", 'profileId', "PerfilId") ORDER BY "Id"), '[]'::jsonb) FROM "tb_Usuarios")
);
"@
    $taskJson = Invoke-ComposeCaptureE16 -Arguments @('exec', '-T', 'postgres', 'psql', '-U', 'fcg_users_demo', '-d', 'fcg_users_demo', '-At', '-c', $taskSql)
    return ConvertFrom-JsonE16 $taskJson
}

function Wait-PublishedE16 {
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
    do {
        $taskSnapshot = Get-DatabaseSnapshotE16
        if ([int]$taskSnapshot.outbox -eq 2 -and [int]$taskSnapshot.published -eq 2) { return $taskSnapshot }
        Start-Sleep -Milliseconds 750
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw [InvalidOperationException]::new($taskEntrega + ': A Outbox não confirmou os dois eventos dentro do prazo.')
}

function Assert-DatabaseSnapshotE16 {
    param([hashtable]$Snapshot)
    Assert-E16 ([int]$Snapshot.migrations -eq 2) 'A demonstração não aplicou as duas migrations esperadas.'
    Assert-E16 ([int]$Snapshot.users -eq 2 -and [int]$Snapshot.admins -eq 1) 'A quantidade de usuários ou administradores divergiu.'
    Assert-E16 ([int]$Snapshot.logs -eq 2) 'A auditoria dos dois cadastros divergiu.'
    Assert-E16 ([int]$Snapshot.outbox -eq 2 -and [int]$Snapshot.published -eq 2) 'A quantidade de eventos publicados divergiu.'
    Assert-E16 (@($Snapshot.events).Count -eq 2 -and @($Snapshot.accounts).Count -eq 2) 'Os identificadores dos registros não foram obtidos.'
    foreach ($taskEvent in $Snapshot.events) {
        Assert-E16 ($taskEvent.eventType -ceq $taskUrn -and [int]$taskEvent.attempts -ge 1) 'A Outbox contém tipo ou tentativa inesperados.'
        Assert-E16 ($taskEvent.eventId -eq $taskEvent.payload.eventId -and $taskEvent.correlationId -eq $taskEvent.payload.correlationId -and $taskEvent.userId -eq $taskEvent.payload.userId) 'Os identificadores do registro e do payload divergem.'
    }
}

function Get-KeyFingerprintsE16 {
    # Somente os hashes saem do contêiner. Nunca lê ou imprime o PEM privado.
    $taskOutput = Invoke-ComposeCaptureE16 -Arguments @('exec', '-T', 'usersapi', 'sha256sum', '/data/jwt/private.pem', '/data/jwt/public.pem')
    $taskLines = @($taskOutput -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-E16 ($taskLines.Count -eq 2) 'Não foi possível verificar os hashes das chaves.'
    $taskHashes = [ordered]@{}
    foreach ($taskLine in $taskLines) {
        Assert-E16 ($taskLine -match '^([a-fA-F0-9]{64})\s+(/data/jwt/(private|public)\.pem)$') 'A saída de verificação de hash foi inesperada.'
        $taskHashes[$Matches[3]] = $Matches[1].ToUpperInvariant()
    }
    Assert-E16 ($taskHashes.Contains('private') -and $taskHashes.Contains('public')) 'Os hashes não cobrem o par completo.'
    return $taskHashes
}

function ConvertFrom-Base64UrlE16 {
    param([string]$Value)
    $taskBase64 = $Value.Replace('-', '+').Replace('_', '/')
    switch ($taskBase64.Length % 4) {
        2 { $taskBase64 += '==' }
        3 { $taskBase64 += '=' }
        1 { throw [InvalidOperationException]::new($taskEntrega + ': O token contém codificação inválida.') }
    }
    return ,([Convert]::FromBase64String($taskBase64))
}

function Assert-JwtE16 {
    param([string]$Token, [string]$UserId, [string]$Role)
    $taskParts = $Token.Split('.')
    Assert-E16 ($taskParts.Length -eq 3) 'O token de acesso não possui formato JWT.'
    $taskHeader = ConvertFrom-JsonE16 ([Text.Encoding]::UTF8.GetString((ConvertFrom-Base64UrlE16 $taskParts[0])))
    $taskClaims = ConvertFrom-JsonE16 ([Text.Encoding]::UTF8.GetString((ConvertFrom-Base64UrlE16 $taskParts[1])))
    Assert-E16 ($taskHeader.alg -ceq 'RS256' -and $taskHeader.kid -ceq 'users-demo-v1') 'O algoritmo ou identificador JWT divergiu.'
    Assert-E16 ($taskClaims.sub -eq $UserId -and $taskClaims.role -ceq $Role) 'O usuário ou perfil do JWT divergiu.'
    Assert-E16 ($taskClaims.iss -ceq 'FIAP.CloudGames' -and $taskClaims.aud -ceq 'FIAP.CloudGames.Api') 'Emissor ou audiência JWT divergiram.'
    Assert-E16 ([long]$taskClaims.exp -gt [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) 'O token emitido já está vencido.'
    $taskPublicPem = Invoke-ComposeCaptureE16 -Arguments @('exec', '-T', 'usersapi', 'cat', '/data/jwt/public.pem')
    $taskRsa = [Security.Cryptography.RSA]::Create()
    try {
        $taskRsa.ImportFromPem($taskPublicPem)
        Assert-E16 ($taskRsa.KeySize -ge 2048) 'A chave pública possui tamanho insuficiente.'
        $taskSignature = ConvertFrom-Base64UrlE16 $taskParts[2]
        $taskSigned = [Text.Encoding]::ASCII.GetBytes($taskParts[0] + '.' + $taskParts[1])
        Assert-E16 ($taskRsa.VerifyData($taskSigned, $taskSignature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) 'A assinatura JWT não validou com a chave pública.'
    }
    finally { $taskRsa.Dispose() }
}

function Get-RabbitQueueE16 {
    $taskResponse = Invoke-HttpE16 -Uri ($taskRabbitUri + '/api/queues/%2F/notifications-user-created') -Headers $taskRabbitHeaders
    Assert-E16 ([int]$taskResponse.StatusCode -eq 200) 'A fila de demonstração não está disponível no RabbitMQ.'
    return ConvertFrom-JsonE16 $taskResponse.Content
}

function Wait-RabbitManagementE17 {
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        try {
            $taskManagement = Invoke-HttpE16 -Uri ($taskRabbitUri + '/api/overview') -Headers $taskRabbitHeaders
            if ([int]$taskManagement.StatusCode -eq 200) { return }
        }
        catch { } # O servidor pode estar vivo antes de o plugin Management concluir seu início.
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw [InvalidOperationException]::new($taskEntrega + ': O Management do broker isolado não ficou disponível no prazo.')
}

function Assert-RabbitRouteAbsentE17 {
    Wait-RabbitManagementE17
    $taskQueueResponse = Invoke-HttpE16 -Uri ($taskRabbitUri + '/api/queues/%2F/notifications-user-created') -Headers $taskRabbitHeaders
    $taskExchangeName = $taskUrn.Substring('urn:message:'.Length)
    $taskExchangeResponse = Invoke-HttpE16 -Uri ($taskRabbitUri + '/api/exchanges/%2F/' + [Uri]::EscapeDataString($taskExchangeName)) -Headers $taskRabbitHeaders
    Assert-E16 ([int]$taskQueueResponse.StatusCode -eq 404 -and [int]$taskExchangeResponse.StatusCode -eq 404) 'Uma fila ou exchange de UserCreated existia antes de o publisher da demonstração iniciar.'
}

function Get-RabbitRouteProofE17 {
    $taskQueue = Get-RabbitQueueE16
    Assert-E16 ($taskQueue.durable -eq $true -and $taskQueue.auto_delete -eq $false -and $taskQueue.exclusive -eq $false -and [int]$taskQueue.consumers -eq 0) 'A fila criada pelo Users não é durável, compartilhável ou está sendo consumida nesta prova.'
    $taskExchangeName = $taskUrn.Substring('urn:message:'.Length)
    $taskExchangeEncoded = [Uri]::EscapeDataString($taskExchangeName)
    $taskExchangeResponse = Invoke-HttpE16 -Uri ($taskRabbitUri + '/api/exchanges/%2F/' + $taskExchangeEncoded) -Headers $taskRabbitHeaders
    Assert-E16 ([int]$taskExchangeResponse.StatusCode -eq 200) 'O publisher não declarou a exchange do contrato.'
    $taskExchange = ConvertFrom-JsonE16 $taskExchangeResponse.Content
    Assert-E16 ($taskExchange.type -ceq 'fanout' -and $taskExchange.durable -eq $true -and $taskExchange.auto_delete -eq $false) 'A exchange do contrato não preserva a topologia fanout durável.'
    $taskBindingsResponse = Invoke-HttpE16 -Uri ($taskRabbitUri + '/api/bindings/%2F/e/' + $taskExchangeEncoded + '/q/notifications-user-created') -Headers $taskRabbitHeaders
    Assert-E16 ([int]$taskBindingsResponse.StatusCode -eq 200) 'Não foi possível consultar a ligação direta entre a exchange e a fila.'
    $taskBindings = @(ConvertFrom-JsonE16 $taskBindingsResponse.Content)
    Assert-E16 ($taskBindings.Count -eq 1 -and $taskBindings[0].source -ceq $taskExchangeName -and $taskBindings[0].destination -ceq 'notifications-user-created' -and $taskBindings[0].destination_type -ceq 'queue') 'O publisher não declarou exatamente uma ligação direta para a fila do contrato.'
    $taskRabbitContainer = Get-ServiceIdE16 'rabbitmq'
    $taskMounts = @(ConvertFrom-JsonE16 (Invoke-DockerCaptureE16 -Arguments @('inspect', '--format', '{{json .Mounts}}', $taskRabbitContainer)))
    Assert-E16 ($taskMounts.Count -eq 1 -and $taskMounts[0].Type -ceq 'volume' -and $taskMounts[0].Destination -ceq '/var/lib/rabbitmq') 'O broker da prova recebeu arquivos de configuração ou volumes inesperados.'
    return [ordered]@{
        exchange = $taskExchangeName; tipo = 'fanout'; duravel = $true; autoDelete = $false
        fila = 'notifications-user-created'; filaDuravel = $true; filaExclusive = $false
        ligacoesDiretas = 1; arquivosExternosMontados = $false; consumers = 0
    }
}
function Wait-RabbitQueueE16 {
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
    do {
        $taskQueue = Get-RabbitQueueE16
        if ([int]$taskQueue.messages -eq 2 -and [int]$taskQueue.messages_ready -eq 2) {
            Assert-E16 ($taskQueue.durable -eq $true -and $taskQueue.auto_delete -eq $false -and [int]$taskQueue.consumers -eq 0) 'A fila não preserva a configuração de demonstração esperada.'
            return $taskQueue
        }
        Start-Sleep -Milliseconds 750
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw [InvalidOperationException]::new($taskEntrega + ': A fila não apresentou os dois eventos dentro do prazo.')
}

function Assert-RabbitEnvelopesE16 {
    param([hashtable]$Snapshot)
    $taskQueue = Wait-RabbitQueueE16
    $taskResponse = Invoke-HttpE16 -Uri ($taskRabbitUri + '/api/queues/%2F/notifications-user-created/get') -Method 'POST' -Headers $taskRabbitHeaders -Body @{ count = 10; ackmode = 'ack_requeue_true'; encoding = 'base64'; truncate = 50000 }
    Assert-E16 ([int]$taskResponse.StatusCode -eq 200) 'Não foi possível consultar os envelopes da fila.'
    $taskMessages = @(ConvertFrom-JsonE16 $taskResponse.Content)
    Assert-E16 ($taskMessages.Count -eq 2) 'A fila não retornou exatamente os dois envelopes esperados.'
    $taskSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($taskMessage in $taskMessages) {
        Assert-E16 ($taskMessage.payload_encoding -ceq 'base64' -and [int]$taskMessage.properties.delivery_mode -eq 2) 'O envelope não é uma mensagem persistente válida.'
        $taskEnvelope = ConvertFrom-JsonE16 ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($taskMessage.payload)))
        Assert-E16 (@($taskEnvelope.messageType) -ccontains $taskUrn) 'O envelope possui uma identidade de contrato diferente.'
        $taskPayload = $taskEnvelope.message
        $taskExpectedFields = @('eventId', 'correlationId', 'occurredAt', 'version', 'userId', 'name', 'email') | Sort-Object
        $taskActualFields = @($taskPayload.Keys) | Sort-Object
        Assert-E16 (($taskActualFields -join '|') -ceq ($taskExpectedFields -join '|')) 'O evento não possui exatamente os sete campos contratados.'
        $taskEvent = @($Snapshot.events | Where-Object { $_.eventId -eq $taskPayload.eventId })
        Assert-E16 ($taskEvent.Count -eq 1 -and $taskSeen.Add([string]$taskPayload.eventId)) 'A fila contém evento desconhecido ou duplicado nesta prova.'
        Assert-E16 ($taskEnvelope.messageId -eq $taskPayload.eventId -and $taskEnvelope.correlationId -eq $taskPayload.correlationId) 'O envelope não preservou as identidades do evento.'
        foreach ($taskField in $taskExpectedFields) {
            if ($taskField -ceq 'occurredAt') {
                Assert-E16 ([DateTimeOffset]::Parse([string]$taskPayload[$taskField]) -eq [DateTimeOffset]::Parse([string]$taskEvent[0].payload[$taskField])) 'O instante do evento divergiu da Outbox.'
            }
            else {
                Assert-E16 ($taskPayload[$taskField] -ceq $taskEvent[0].payload[$taskField]) ('O campo ' + $taskField + ' divergiu da Outbox.')
            }
        }
    }
    # ack_requeue_true consulta e recoloca cada mensagem; nenhuma mensagem é removida da fila.
    return [ordered]@{ mensagens = [int]$taskQueue.messages; eventos = @($taskSeen | Sort-Object); consulta = 'ack_requeue_true; mensagens preservadas' }
}

function Get-SafeDatabaseSummaryE16 {
    param([hashtable]$Snapshot)
    return [ordered]@{
        migrations = [int]$Snapshot.migrations; usuarios = [int]$Snapshot.users; administradores = [int]$Snapshot.admins
        auditorias = [int]$Snapshot.logs; outbox = [int]$Snapshot.outbox; publicados = [int]$Snapshot.published
        usuariosIds = @($Snapshot.accounts | ForEach-Object { $_.id } | Sort-Object)
        eventos = @($Snapshot.events | ForEach-Object { [ordered]@{ id = $_.id; eventId = $_.eventId; correlationId = $_.correlationId; userId = $_.userId; attempts = [int]$_.attempts } })
    }
}

New-Item -ItemType Directory -Path $taskSource -Force | Out-Null
Push-Location $taskRepo
try {
    $taskFiles = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    Assert-E16 ($LASTEXITCODE -eq 0) 'Não foi possível inventariar a fonte Git.'
    foreach ($taskRelative in $taskFiles) {
        if ($taskRelative -match '(^|/)(\.git|\.codex|\.agents|\.aws|\.local|secrets|bin|obj|TestResults|coverage)(/|$)' -or
            $taskRelative -match '(^|/)\.env($|\.)|\.(pem|key|pfx|p12|log|tmp)$|(^|/)(secrets\.json|appsettings\..*Local\.json)$') { continue }
        $taskOriginal = [IO.Path]::GetFullPath((Join-Path $taskRepo $taskRelative))
        $taskDestination = [IO.Path]::GetFullPath((Join-Path $taskSource $taskRelative))
        Assert-E16 ($taskOriginal.StartsWith($taskRepo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
            $taskDestination.StartsWith($taskSource + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Um caminho de cópia ficou fora da área da prova.'
        if (-not (Test-Path -LiteralPath $taskOriginal -PathType Leaf)) { continue }
        $taskAncestor = $taskOriginal
        while ($taskAncestor -ne $taskRepo) {
            Assert-E16 (((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'A cópia não segue links de arquivos ou diretórios.'
            $taskAncestor = Split-Path -Parent $taskAncestor
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
        Copy-Item -LiteralPath $taskOriginal -Destination $taskDestination
        $taskManifest.Add([ordered]@{ arquivo = $taskRelative; sha256 = (Get-FileHash -LiteralPath $taskDestination -Algorithm SHA256).Hash })
    }
    Assert-E16 ((Test-Path -LiteralPath $taskComposeFile) -and (Test-Path -LiteralPath (Join-Path $taskSource 'Dockerfile'))) 'A cópia não possui Compose e Dockerfile.'
    $taskManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskResults ('manifesto-fonte-' + $taskEntregaTag + '.json')) -Encoding utf8
    Assert-E16 ($taskProject -match '^fcg-users-e1[67]-[a-f0-9]{32}$') 'O projeto temporário possui nome inesperado.'
    $taskExisting = Invoke-DockerCaptureE16 -Arguments @('ps', '--all', '--filter', ('label=com.docker.compose.project=' + $taskProject), '--format', '{{.ID}}')
    Assert-E16 ([string]::IsNullOrWhiteSpace($taskExisting)) 'O projeto temporário já possui recursos; a prova não os reutilizará.'
    $taskHttpPort = Get-FreePortE16
    do { $taskRabbitPort = Get-FreePortE16 } while ($taskRabbitPort -eq $taskHttpPort)
    [Environment]::SetEnvironmentVariable('FCG_USERS_HTTP_PORT', [string]$taskHttpPort)
    [Environment]::SetEnvironmentVariable('FCG_RABBIT_MANAGEMENT_PORT', [string]$taskRabbitPort)
    $taskApiUri = 'http://127.0.0.1:' + $taskHttpPort
    $taskRabbitUri = 'http://127.0.0.1:' + $taskRabbitPort
    $taskRabbitHeaders = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('fcg_demo:Rabbit_Demo_2026!')) }
    $taskOwnProject = $true

    $taskStage = 'estrutura Compose e guardas antes da instalação nova'
    Write-Host ($taskEntrega + ': verificando a imagem única e as guardas em uma cópia limpa com volumes novos.')
    $taskConfiguration = ConvertFrom-JsonE16 (Invoke-ComposeCaptureE16 -Arguments @('config', '--format', 'json'))
    Assert-E16 ((@($taskConfiguration.services.Keys | Sort-Object) -join '|') -ceq 'postgres|rabbitmq|usersapi') 'O Compose isolado precisa conter somente PostgreSQL, RabbitMQ e UsersAPI, sem serviço preparacao.'
    if ($SemArquivosRabbitMq) {
        # Exclusivo da cópia em TestResults: não modifica o broker/Compose do usuário ou do grupo.
        # !override substitui toda a lista para não herdar os bind mounts de configuração/definitions.
        $taskRabbitOverrideFile = Join-Path $taskSource '.rabbit-sem-arquivos.compose.yaml'
        $taskRabbitOverride = @"
services:
  rabbitmq:
    environment:
      RABBITMQ_DEFAULT_USER: fcg_demo
      RABBITMQ_DEFAULT_PASS: "Rabbit_Demo_2026!"
    volumes: !override
      - rabbitmq-dados:/var/lib/rabbitmq
"@
        [IO.File]::WriteAllText($taskRabbitOverrideFile, $taskRabbitOverride + "`n", [Text.UTF8Encoding]::new($false))
        $taskComposeArguments += @('--file', $taskRabbitOverrideFile)
        $taskConfiguration = ConvertFrom-JsonE16 (Invoke-ComposeCaptureE16 -Arguments @('config', '--format', 'json'))
        $taskRabbitVolumes = @($taskConfiguration.services.rabbitmq.volumes)
        Assert-E16 ($taskRabbitVolumes.Count -eq 1 -and $taskRabbitVolumes[0].type -ceq 'volume' -and $taskRabbitVolumes[0].target -ceq '/var/lib/rabbitmq') 'A prova sem arquivos herdou uma montagem de configuração do RabbitMQ.'
    }
    $taskGuardFile = Join-Path $taskSource ('.guard-' + $taskEntregaTag + '.compose.yaml')
    # Os casos negativos não podem ser reiniciados automaticamente pelo Docker.
    [IO.File]::WriteAllText($taskGuardFile, "services:`n  usersapi:`n    restart: `"no`"`n  postgres:`n    restart: `"no`"`n", [Text.UTF8Encoding]::new($false))
    $taskGuardComposeArguments = $taskComposeArguments + @('--file', $taskGuardFile)
    Invoke-ComposeVisibleE16 -Arguments @('build', 'usersapi')
    Invoke-ComposeVisibleE16 -Arguments @('up', '-d', 'postgres', 'rabbitmq')
    Wait-DependenciesE16
    if ($SemArquivosRabbitMq) { Assert-RabbitRouteAbsentE17 }
    foreach ($taskGuard in @(
        @{ nome = 'DOTNET Production e demonstração habilitada'; variaveis = @('DOTNET_ENVIRONMENT=Production', 'DemonstracaoLocal__Habilitada=true'); exit = 1 },
        @{ nome = 'ASPNETCORE Production com DOTNET Development'; variaveis = @('ASPNETCORE_ENVIRONMENT=Production', 'DemonstracaoLocal__Habilitada=true'); exit = 1 },
        @{ nome = 'Flag de demonstração inválida'; variaveis = @('DemonstracaoLocal__Habilitada=invalido'); exit = 1 },
        @{ nome = 'Flag false com banco e volume JWT vazios'; variaveis = @('DemonstracaoLocal__Habilitada=false', 'Outbox__Enabled=false'); exit = -1 }
    )) {
        $taskGuardName = Start-GuardContainerE16 -Environment $taskGuard.variaveis
        try {
            $taskGuardExit = Wait-GuardExitE16 -Name $taskGuardName -ExpectedExit $taskGuard.exit
            $taskGuardLogs = Invoke-DockerCaptureE16 -Arguments @('logs', $taskGuardName)
            Assert-E16 ($taskGuardLogs -notmatch 'Par de chaves JWT da demonstração pronto|Migrations da demonstração aplicadas|Preparação da demonstração concluída\.') 'Uma guarda recusada ou desabilitada executou a preparação.'
            Assert-NoPreparationE16
            if ($SemArquivosRabbitMq) { Assert-RabbitRouteAbsentE17 }
            $taskGuardResults.Add([ordered]@{ caso = $taskGuard.nome; exit = $taskGuardExit; tabelasOuChavesCriadas = $false })
        }
        finally { if ($taskGuardContainers.Contains($taskGuardName)) { Remove-GuardContainerE16 $taskGuardName } }
    }
    Invoke-CancellationGuardE16
    if ($SemArquivosRabbitMq) { Assert-RabbitRouteAbsentE17 }
    $taskChecks.Add('Guarda em banco/volume JWT vazios: Production, ambientes contraditórios e flag inválida recusados; flag false não prepara; nenhuma tabela ou chave criada.')
    $taskChecks.Add('SIGTERM durante espera pela trava: bootstrap cancelado com exit 143, sem iniciar HTTP ou criar tabelas/chaves; sessão exclusiva da prova removida.')

    $taskStage = 'primeira inicialização Compose'
    Invoke-ComposeVisibleE16 -Arguments @('up', '--build', '-d')
    Wait-ReadyE16
    $taskChecks.Add('Instalação nova em um container Users: preparação automática concluída, /health e Swagger HTTP 200; nenhum serviço preparacao no Compose.')

    $taskStage = 'cadastro, login e autorização'
    Write-Host ($taskEntrega + ': verificando cadastro, login, permissões e assinatura JWT.')
    $taskAdminResponse = Invoke-HttpE16 -Uri ($taskApiUri + '/api/v1/auth/login') -Method 'POST' -Body @{ email = 'admin@fcg.local'; senha = 'Admin_Demo_2026!' }
    Assert-E16 ([int]$taskAdminResponse.StatusCode -eq 200) 'O login do administrador sintético falhou.'
    $taskAdmin = ConvertFrom-JsonE16 $taskAdminResponse.Content
    Assert-JwtE16 -Token $taskAdmin.accessToken -UserId $taskAdmin.usuario.id -Role 'Administrador'
    $taskCorrelation = [guid]::NewGuid().ToString('D')
    $taskCreationResponse = Invoke-HttpE16 -Uri ($taskApiUri + '/api/v1/usuarios') -Method 'POST' -Headers @{ 'X-Correlation-ID' = $taskCorrelation } -Body @{
        nome = ('Pessoa Demonstração ' + $taskEntrega); cpf = '11144477735'; dataNascimento = '2000-01-01'
        email = ('pessoa-' + $taskEntregaTag + '@exemplo.test'); senha = 'Usuario_Demo_2026!'
    }
    Assert-E16 ([int]$taskCreationResponse.StatusCode -eq 201) 'O cadastro público sintético falhou.'
    Assert-E16 ([string]$taskCreationResponse.Headers['X-Correlation-ID'] -eq $taskCorrelation) 'A API não devolveu a correlação informada.'
    $taskUser = ConvertFrom-JsonE16 $taskCreationResponse.Content
    $taskLoginResponse = Invoke-HttpE16 -Uri ($taskApiUri + '/api/v1/auth/login') -Method 'POST' -Body @{ email = ('pessoa-' + $taskEntregaTag + '@exemplo.test'); senha = 'Usuario_Demo_2026!' }
    Assert-E16 ([int]$taskLoginResponse.StatusCode -eq 200) 'O login do usuário sintético falhou.'
    $taskLogin = ConvertFrom-JsonE16 $taskLoginResponse.Content
    Assert-JwtE16 -Token $taskLogin.accessToken -UserId $taskUser.id -Role 'Usuario'
    $taskUserHeaders = @{ Authorization = 'Bearer ' + $taskLogin.accessToken }
    $taskAdminHeaders = @{ Authorization = 'Bearer ' + $taskAdmin.accessToken }
    Assert-E16 ([int](Invoke-HttpE16 -Uri ($taskApiUri + '/api/v1/usuarios/' + $taskUser.id)).StatusCode -eq 401) 'Uma consulta sem token não foi recusada.'
    Assert-E16 ([int](Invoke-HttpE16 -Uri ($taskApiUri + '/api/v1/usuarios/' + $taskUser.id) -Headers $taskUserHeaders).StatusCode -eq 200) 'O usuário não conseguiu consultar seu cadastro.'
    Assert-E16 ([int](Invoke-HttpE16 -Uri ($taskApiUri + '/api/v1/usuarios/' + $taskUser.id + '/perfil') -Method 'PUT' -Headers $taskUserHeaders -Body @{ perfilId = '22222222-2222-2222-2222-222222222222' }).StatusCode -eq 403) 'O usuário comum conseguiu acessar uma ação administrativa.'
    Assert-E16 ([int](Invoke-HttpE16 -Uri ($taskApiUri + '/api/v1/usuarios/' + $taskUser.id) -Headers $taskAdminHeaders).StatusCode -eq 200) 'O administrador não conseguiu consultar o usuário.'
    $taskChecks.Add('Cadastro HTTP 201; logins HTTP 200; acesso anônimo 401, titular 200, ação administrativa por usuário comum 403 e administrador 200.')
    $taskChecks.Add('JWT dos dois perfis validado com apenas a chave pública: RSA >= 2048, RS256, kid, sub, role, issuer, audience e exp.')

    $taskStage = 'Outbox, fila e isolamento das chaves'
    Write-Host ($taskEntrega + ': comparando banco, eventos publicados e mensagens persistentes no RabbitMQ.')
    $taskBefore = Wait-PublishedE16
    Assert-DatabaseSnapshotE16 $taskBefore
    $taskCreatedEvent = @($taskBefore.events | Where-Object { $_.userId -eq $taskUser.id })
    Assert-E16 ($taskCreatedEvent.Count -eq 1 -and $taskCreatedEvent[0].correlationId -eq $taskCorrelation) 'A Outbox não preservou a correlação do cadastro público.'
    $taskQueueBefore = Assert-RabbitEnvelopesE16 $taskBefore
    if ($SemArquivosRabbitMq) {
        $taskRouteBefore = Get-RabbitRouteProofE17
        $taskChecks.Add('RabbitMQ novo sem JSON/conf montados: fila/exchange ausentes antes do publisher; Users declarou rota direta durável fanout, com duas mensagens prontas e sem consumidor Notifications.')
    }
    $taskHashesBefore = Get-KeyFingerprintsE16
    $taskApiContainer = Get-ServiceIdE16 'usersapi'
    $taskMounts = @(ConvertFrom-JsonE16 (Invoke-DockerCaptureE16 -Arguments @('inspect', '--format', '{{json .Mounts}}', $taskApiContainer)))
    $taskJwtMount = @($taskMounts | Where-Object { $_.Destination -ceq '/data/jwt' })
    Assert-E16 ($taskJwtMount.Count -eq 1 -and $taskJwtMount[0].RW -eq $true -and $taskJwtMount[0].Type -ceq 'volume') 'O Users não recebeu o volume JWT gravável necessário à inicialização integrada.'
    $taskUid = Invoke-ComposeCaptureE16 -Arguments @('exec', '-T', 'usersapi', 'id', '-u')
    Assert-E16 ($taskUid -ceq '1654') 'A API não está executando com o usuário não privilegiado esperado.'
    $taskModes = Invoke-ComposeCaptureE16 -Arguments @('exec', '-T', 'usersapi', 'stat', '-c', '%a:%u', '/data/jwt', '/data/jwt/private.pem', '/data/jwt/public.pem')
    Assert-E16 (($taskModes -split '\r?\n' -join '|') -ceq '700:1654|600:1654|644:1654') 'As permissões ou proprietários do diretório e das chaves divergem.'
    $taskPidOne = Invoke-ComposeCaptureE16 -Arguments @('exec', '-T', 'usersapi', '/bin/sh', '-c', 'tr "\000" " " < /proc/1/cmdline')
    Assert-E16 ($taskPidOne -match '^dotnet\s+/app/FCG\.Users\.Api\.dll(?:\s|$)') 'A API não assumiu PID 1 após o término da preparação.'
    $taskServices = Invoke-ComposeCaptureE16 -Arguments @('ps', '--all', '--services')
    Assert-E16 ((@($taskServices -split '\r?\n' | Where-Object { $_ } | Sort-Object) -join '|') -ceq 'postgres|rabbitmq|usersapi') 'A prova deixou um serviço de preparação ou guarda ativo na instalação final.'
    $taskChecks.Add('Duas migrations, dois usuários, um administrador, duas auditorias e dois eventos publicados; IDs da Outbox e dos envelopes iguais, com os sete campos do contrato e URN esperado.')
    $taskChecks.Add('Fila durável e mensagens persistentes; consulta ack_requeue_true preserva as mensagens, sem simular consumo ou email.')
    $taskChecks.Add('Users executa como UID 1654, volume JWT gravável, diretório 700, chave privada 600 e pública 644; API assume PID 1 após o bootstrap; somente fingerprints são registrados.')

    $taskStage = 'reinício preservando volumes'
    Write-Host ($taskEntrega + ': recriando os contêineres e conferindo preservação dos dados, chaves e mensagens.')
    [void](Invoke-ComposeCaptureE16 -Arguments @('down')) # Sem --volumes: esta etapa precisa preservar os dados.
    Invoke-ComposeVisibleE16 -Arguments @('up', '--build', '-d')
    Wait-ReadyE16
    $taskAfter = Wait-PublishedE16
    Assert-DatabaseSnapshotE16 $taskAfter
    $taskHashesAfter = Get-KeyFingerprintsE16
    Assert-E16 ($taskHashesBefore.private -ceq $taskHashesAfter.private -and $taskHashesBefore.public -ceq $taskHashesAfter.public) 'O reinício alterou o par de chaves.'
    $taskSafeBefore = Get-SafeDatabaseSummaryE16 $taskBefore
    $taskSafeAfter = Get-SafeDatabaseSummaryE16 $taskAfter
    Assert-E16 (($taskSafeBefore | ConvertTo-Json -Depth 8 -Compress) -ceq ($taskSafeAfter | ConvertTo-Json -Depth 8 -Compress)) 'O reinício alterou ou duplicou usuários, auditorias ou eventos.'
    $taskQueueAfter = Assert-RabbitEnvelopesE16 $taskAfter
    if ($SemArquivosRabbitMq) {
        $taskRouteAfter = Get-RabbitRouteProofE17
        Assert-E16 (($taskRouteBefore | ConvertTo-Json -Depth 5 -Compress) -ceq ($taskRouteAfter | ConvertTo-Json -Depth 5 -Compress)) 'A rota direta da demonstração divergiu após reiniciar o broker e a API.'
    }
    Assert-E16 (($taskQueueBefore.eventos -join '|') -ceq ($taskQueueAfter.eventos -join '|')) 'O reinício alterou os eventos presentes na fila.'
    Assert-E16 ([int](Invoke-HttpE16 -Uri ($taskApiUri + '/api/v1/usuarios/' + $taskUser.id) -Headers $taskUserHeaders).StatusCode -eq 200) 'O JWT emitido antes do reinício deixou de validar.'
    $taskAdminAgainResponse = Invoke-HttpE16 -Uri ($taskApiUri + '/api/v1/auth/login') -Method 'POST' -Body @{ email = 'admin@fcg.local'; senha = 'Admin_Demo_2026!' }
    Assert-E16 ([int]$taskAdminAgainResponse.StatusCode -eq 200) 'O administrador não conseguiu entrar após o reinício.'
    $taskAdminAgain = ConvertFrom-JsonE16 $taskAdminAgainResponse.Content
    Assert-E16 ($taskAdminAgain.usuario.id -eq $taskAdmin.usuario.id) 'O administrador foi substituído no reinício.'
    $taskChecks.Add('Compose down sem remover volumes + up --build -d: mesmos usuários, auditorias, eventos, tentativas e hashes; mesma conta administrativa e JWT antigo ainda aceito.')
    $taskChecks.Add('RabbitMQ preserva os mesmos dois eventos após recriação dos contêineres; nenhuma mensagem adicional foi criada pela inicialização integrada.')

    $taskStage = 'inicialização normal sem bootstrap em ambiente preparado'
    Write-Host ($taskEntrega + ': verificando que flag false inicia normalmente a API, sem executar preparação.')
    $taskNormalName = Start-GuardContainerE16 -Environment @('DemonstracaoLocal__Habilitada=false', 'Outbox__Enabled=false')
    try {
        Wait-GuardHealthyE16 -Name $taskNormalName
        $taskNormalPid = Invoke-DockerCaptureE16 -Arguments @('exec', $taskNormalName, '/bin/sh', '-c', 'tr "\000" " " < /proc/1/cmdline')
        Assert-E16 ($taskNormalPid -match '^dotnet\s+/app/FCG\.Users\.Api\.dll(?:\s|$)') 'A inicialização sem bootstrap não executou a API diretamente como PID 1.'
        $taskNormalLogs = Invoke-DockerCaptureE16 -Arguments @('logs', $taskNormalName)
        Assert-E16 ($taskNormalLogs -notmatch 'Par de chaves JWT da demonstração pronto|Migrations da demonstração aplicadas|Preparação da demonstração concluída\.') 'Flag false executou o bootstrap no ambiente já preparado.'
        $taskGuardResults.Add([ordered]@{ caso = 'Flag false com banco/chaves existentes'; apiSaudavel = $true; bootstrapExecutado = $false; outboxAtivadaNestaInstancia = $false })
    }
    finally { if ($taskGuardContainers.Contains($taskNormalName)) { Remove-GuardContainerE16 $taskNormalName } }
    $taskFinal = Get-DatabaseSnapshotE16
    Assert-DatabaseSnapshotE16 $taskFinal
    Assert-E16 (((Get-SafeDatabaseSummaryE16 $taskFinal) | ConvertTo-Json -Depth 8 -Compress) -ceq ($taskSafeAfter | ConvertTo-Json -Depth 8 -Compress)) 'Uma execução recusada alterou registros.'
    $taskFinalHashes = Get-KeyFingerprintsE16
    Assert-E16 ($taskFinalHashes.private -ceq $taskHashesAfter.private -and $taskFinalHashes.public -ceq $taskHashesAfter.public) 'Uma execução recusada alterou as chaves.'
    $taskChecks.Add('Flag false em ambiente preparado inicia API saudável como PID 1, sem executar migrations/bootstrap; registros e hashes permanecem intactos.')

    $taskImages = @('postgres', 'rabbitmq', 'usersapi') | ForEach-Object {
        $taskService = $_
        $taskImage = Invoke-DockerCaptureE16 -Arguments @('inspect', '--format', '{{.Image}}', (Get-ServiceIdE16 $taskService))
        [ordered]@{ servico = $taskService; imagemId = $taskImage }
    }
    $taskSummary = [ordered]@{
        entrega = $taskEntrega; resultado = 'aprovado'; inicioUtc = $taskStarted.ToString('o')
        fonte = 'Cópia da working tree atual via git ls-files; sem .git, .env, chaves, bin, obj ou TestResults. Não é clone de um commit publicado.'
        projetoDescartavel = $taskProject; arquivosFonte = $taskManifest.Count; manifesto = $taskManifest.ToArray()
        comando = 'docker compose up --build -d, com projeto e arquivo da cópia temporária e portas livres.'
        verificacoesAprovadas = $taskChecks.ToArray()
        bancoAntesReinicio = $taskSafeBefore; bancoDepoisReinicio = $taskSafeAfter
        chaves = [ordered]@{ antes = $taskHashesBefore; depois = $taskHashesAfter; privadaLidaOuImpressa = $false; volumeApiSomenteLeitura = $false; usuarioApiUid = 1654; diretorioModo = '700'; privadaModo = '600'; publicaModo = '644'; apiPid = 1 }
        fila = [ordered]@{ antes = $taskQueueBefore; depois = $taskQueueAfter; consumidorNotificationsExecutado = $false; emailEnviado = $false }
        reinicializacoes = 1; preparacao = 'Automática na mesma imagem e container Users; nenhum serviço separado.'
        guardasInicializacao = $taskGuardResults.ToArray()
        rabbitMqSemArquivosMontados = [bool]$SemArquivosRabbitMq
        topologiaDeclaradaPeloPublisher = if ($SemArquivosRabbitMq) { [ordered]@{ antesReinicio = $taskRouteBefore; depoisReinicio = $taskRouteAfter; queueOuExchangeExistiamAntesPublisher = $false } } else { $null }
        imagens = @($taskImages)
    }
}
catch {
    $taskException = $_.Exception
    $taskDetail = if ($taskException.Message.StartsWith($taskEntrega + ':', [StringComparison]::Ordinal)) { $taskException.Message } else { $taskException.GetType().Name }
    $taskFailure = 'A verificação ' + $taskEntrega + ' falhou durante ' + $taskStage + '. ' + $taskDetail
}
finally {
    try {
        if ($taskOwnProject) {
            $taskCleanupFailed = $false
            foreach ($taskGuardName in @($taskGuardContainers.ToArray())) {
                try { Remove-GuardContainerE16 $taskGuardName }
                catch { $taskCleanupFailed = $true }
            }
            # Somente o projeto aleatório desta execução. A pasta de fontes e os resultados são preservados.
            # Mesmo que uma guarda não possa ser removida individualmente, tenta concluir a limpeza Compose.
            Assert-E16 ($taskProject -match '^fcg-users-e1[67]-[a-f0-9]{32}$' -and $taskComposeFile.StartsWith($taskSource + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'O alvo da limpeza descartável não foi confirmado.'
            [void](Invoke-ComposeCaptureE16 -Arguments @('down', '--volumes', '--remove-orphans'))
            Assert-E16 (-not $taskCleanupFailed) 'Não foi possível confirmar a remoção individual de todas as guardas descartáveis.'
        }
    }
    catch {
        $taskCleanupMessage = 'A verificação ' + $taskEntrega + ' não conseguiu confirmar a limpeza completa do projeto descartável ' + $taskProject + '.'
        $taskFailure = if ($null -eq $taskFailure) { $taskCleanupMessage } else { $taskFailure + ' ' + $taskCleanupMessage }
    }
    finally {
        [Environment]::SetEnvironmentVariable('FCG_USERS_HTTP_PORT', $taskOldHttpPort)
        [Environment]::SetEnvironmentVariable('FCG_RABBIT_MANAGEMENT_PORT', $taskOldRabbitPort)
        Pop-Location
    }
}

if ($null -ne $taskFailure) { throw [InvalidOperationException]::new($taskFailure) }
Assert-E16 ($null -ne $taskSummary) 'A execução terminou sem uma evidência completa.'
$taskSummary.fimUtc = [DateTimeOffset]::UtcNow.ToString('o')
$taskSummary.limpeza = 'Contêineres, rede e volumes somente do projeto descartável removidos; fonte e evidências locais preservadas.'
$taskReport = Join-Path $taskResults ('demonstracao-' + $taskEntregaTag + '.json')
$taskSummary | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $taskReport -Encoding utf8
Write-Host ($taskEntrega + ': todas as verificações aprovadas e recursos descartáveis removidos. Relatório: ' + $taskReport)

