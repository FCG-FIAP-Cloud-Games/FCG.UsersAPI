#requires -Version 7.4
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$taskRepo = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$taskResults = Join-Path $taskRepo 'TestResults/E15'
$taskId = [guid]::NewGuid().ToString('N')
$taskSource = Join-Path $taskResults ('fonte-' + $taskId)
$taskProject = 'fcg-users-e15-' + $taskId
$taskComposeFile = Join-Path $taskSource 'compose.yaml'
$taskComposeArguments = @('compose', '--project-name', $taskProject, '--file', $taskComposeFile)
$taskStarted = [DateTimeOffset]::UtcNow
$taskOldHttpPort = [Environment]::GetEnvironmentVariable('FCG_USERS_HTTP_PORT')
$taskOldRabbitPort = [Environment]::GetEnvironmentVariable('FCG_RABBIT_MANAGEMENT_PORT')
$taskManifest = [Collections.Generic.List[object]]::new()
$taskChecks = [Collections.Generic.List[string]]::new()
$taskOwnProject = $false
$taskFailure = $null
$taskSummary = $null
$taskStage = 'inventário da fonte'
$taskUrn = 'urn:message:FCG.Notifications.Application.Messaging.Contracts:UserCreatedEvent'

function Assert-E15 {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw [InvalidOperationException]::new('E15: ' + $Message) }
}

function ConvertFrom-JsonE15 {
    param([string]$Json)
    $taskParameters = @{ InputObject = $Json; AsHashtable = $true }
    if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $taskParameters.DateKind = 'String' }
    return ConvertFrom-Json @taskParameters
}

function Invoke-DockerCaptureE15 {
    param([string[]]$Arguments)
    $taskOutput = @(& docker @Arguments 2>&1)
    $taskExit = $LASTEXITCODE
    Assert-E15 ($taskExit -eq 0) ('Uma consulta Docker falhou, código ' + $taskExit + '.')
    return ($taskOutput | Out-String).Trim()
}

function Invoke-ComposeCaptureE15 {
    param([string[]]$Arguments)
    return Invoke-DockerCaptureE15 -Arguments ($taskComposeArguments + $Arguments)
}

function Invoke-ComposeVisibleE15 {
    param([string[]]$Arguments)
    & docker @taskComposeArguments @Arguments
    Assert-E15 ($LASTEXITCODE -eq 0) 'O comando Compose da demonstração falhou.'
}

function Get-FreePortE15 {
    $taskListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $taskListener.Start()
        return ([Net.IPEndPoint]$taskListener.LocalEndpoint).Port
    }
    finally { $taskListener.Stop() }
}

function Get-ServiceIdE15 {
    param([string]$Service)
    $taskContainer = Invoke-ComposeCaptureE15 -Arguments @('ps', '--all', '--quiet', $Service)
    Assert-E15 ($taskContainer -match '^[a-f0-9]{12,64}$') ('O serviço ' + $Service + ' não possui exatamente um contêiner da prova.')
    return $taskContainer
}

function Get-ServiceStateE15 {
    param([string]$Service)
    $taskContainer = Get-ServiceIdE15 -Service $Service
    return ConvertFrom-JsonE15 (Invoke-DockerCaptureE15 -Arguments @('inspect', '--format', '{{json .State}}', $taskContainer))
}

function Invoke-HttpE15 {
    param([string]$Uri, [string]$Method = 'GET', [hashtable]$Headers = @{}, [object]$Body = $null)
    $taskParameters = @{ Uri = $Uri; Method = $Method; Headers = $Headers; SkipHttpErrorCheck = $true; TimeoutSec = 10 }
    if ($null -ne $Body) {
        $taskParameters.Body = $Body | ConvertTo-Json -Depth 8 -Compress
        $taskParameters.ContentType = 'application/json; charset=utf-8'
    }
    try { return Invoke-WebRequest @taskParameters }
    catch { throw [InvalidOperationException]::new('E15: Uma chamada HTTP da prova não foi concluída.') }
}

function Wait-ReadyE15 {
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(180)
    do {
        $taskSetup = Get-ServiceStateE15 -Service 'preparacao'
        Assert-E15 ($taskSetup.Status -ne 'exited' -or [int]$taskSetup.ExitCode -eq 0) 'O preparador encerrou sem sucesso.'
        $taskApiState = Get-ServiceStateE15 -Service 'usersapi'
        Assert-E15 ($taskApiState.Status -notin @('dead', 'exited')) 'A API encerrou durante a inicialização.'
        if ($taskSetup.Status -eq 'exited' -and [int]$taskSetup.ExitCode -eq 0 -and $taskApiState.Health.Status -eq 'healthy') {
            $taskHealth = Invoke-HttpE15 -Uri ($taskApiUri + '/health')
            if ([int]$taskHealth.StatusCode -eq 200) {
                $taskSwagger = Invoke-HttpE15 -Uri ($taskApiUri + '/swagger/index.html')
                Assert-E15 ([int]$taskSwagger.StatusCode -eq 200) 'O Swagger da demonstração não está disponível.'
                return
            }
        }
        Start-Sleep -Milliseconds 750
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw [InvalidOperationException]::new('E15: A demonstração não ficou pronta dentro do prazo.')
}

function Get-DatabaseSnapshotE15 {
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
    $taskJson = Invoke-ComposeCaptureE15 -Arguments @('exec', '-T', 'postgres', 'psql', '-U', 'fcg_users_demo', '-d', 'fcg_users_demo', '-At', '-c', $taskSql)
    return ConvertFrom-JsonE15 $taskJson
}

function Wait-PublishedE15 {
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
    do {
        $taskSnapshot = Get-DatabaseSnapshotE15
        if ([int]$taskSnapshot.outbox -eq 2 -and [int]$taskSnapshot.published -eq 2) { return $taskSnapshot }
        Start-Sleep -Milliseconds 750
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw [InvalidOperationException]::new('E15: A Outbox não confirmou os dois eventos dentro do prazo.')
}

function Assert-DatabaseSnapshotE15 {
    param([hashtable]$Snapshot)
    Assert-E15 ([int]$Snapshot.migrations -eq 2) 'A demonstração não aplicou as duas migrations esperadas.'
    Assert-E15 ([int]$Snapshot.users -eq 2 -and [int]$Snapshot.admins -eq 1) 'A quantidade de usuários ou administradores divergiu.'
    Assert-E15 ([int]$Snapshot.logs -eq 2) 'A auditoria dos dois cadastros divergiu.'
    Assert-E15 ([int]$Snapshot.outbox -eq 2 -and [int]$Snapshot.published -eq 2) 'A quantidade de eventos publicados divergiu.'
    Assert-E15 (@($Snapshot.events).Count -eq 2 -and @($Snapshot.accounts).Count -eq 2) 'Os identificadores dos registros não foram obtidos.'
    foreach ($taskEvent in $Snapshot.events) {
        Assert-E15 ($taskEvent.eventType -ceq $taskUrn -and [int]$taskEvent.attempts -ge 1) 'A Outbox contém tipo ou tentativa inesperados.'
        Assert-E15 ($taskEvent.eventId -eq $taskEvent.payload.eventId -and $taskEvent.correlationId -eq $taskEvent.payload.correlationId -and $taskEvent.userId -eq $taskEvent.payload.userId) 'Os identificadores do registro e do payload divergem.'
    }
}

function Get-KeyFingerprintsE15 {
    # Somente os hashes saem do contêiner. Nunca lê ou imprime o PEM privado.
    $taskOutput = Invoke-ComposeCaptureE15 -Arguments @('exec', '-T', 'usersapi', 'sha256sum', '/data/jwt/private.pem', '/data/jwt/public.pem')
    $taskLines = @($taskOutput -split '\r?\n' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    Assert-E15 ($taskLines.Count -eq 2) 'Não foi possível verificar os hashes das chaves.'
    $taskHashes = [ordered]@{}
    foreach ($taskLine in $taskLines) {
        Assert-E15 ($taskLine -match '^([a-fA-F0-9]{64})\s+(/data/jwt/(private|public)\.pem)$') 'A saída de verificação de hash foi inesperada.'
        $taskHashes[$Matches[3]] = $Matches[1].ToUpperInvariant()
    }
    Assert-E15 ($taskHashes.Contains('private') -and $taskHashes.Contains('public')) 'Os hashes não cobrem o par completo.'
    return $taskHashes
}

function ConvertFrom-Base64UrlE15 {
    param([string]$Value)
    $taskBase64 = $Value.Replace('-', '+').Replace('_', '/')
    switch ($taskBase64.Length % 4) {
        2 { $taskBase64 += '==' }
        3 { $taskBase64 += '=' }
        1 { throw [InvalidOperationException]::new('E15: O token contém codificação inválida.') }
    }
    return ,([Convert]::FromBase64String($taskBase64))
}

function Assert-JwtE15 {
    param([string]$Token, [string]$UserId, [string]$Role)
    $taskParts = $Token.Split('.')
    Assert-E15 ($taskParts.Length -eq 3) 'O token de acesso não possui formato JWT.'
    $taskHeader = ConvertFrom-JsonE15 ([Text.Encoding]::UTF8.GetString((ConvertFrom-Base64UrlE15 $taskParts[0])))
    $taskClaims = ConvertFrom-JsonE15 ([Text.Encoding]::UTF8.GetString((ConvertFrom-Base64UrlE15 $taskParts[1])))
    Assert-E15 ($taskHeader.alg -ceq 'RS256' -and $taskHeader.kid -ceq 'users-demo-v1') 'O algoritmo ou identificador JWT divergiu.'
    Assert-E15 ($taskClaims.sub -eq $UserId -and $taskClaims.role -ceq $Role) 'O usuário ou perfil do JWT divergiu.'
    Assert-E15 ($taskClaims.iss -ceq 'FIAP.CloudGames' -and $taskClaims.aud -ceq 'FIAP.CloudGames.Api') 'Emissor ou audiência JWT divergiram.'
    Assert-E15 ([long]$taskClaims.exp -gt [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()) 'O token emitido já está vencido.'
    $taskPublicPem = Invoke-ComposeCaptureE15 -Arguments @('exec', '-T', 'usersapi', 'cat', '/data/jwt/public.pem')
    $taskRsa = [Security.Cryptography.RSA]::Create()
    try {
        $taskRsa.ImportFromPem($taskPublicPem)
        Assert-E15 ($taskRsa.KeySize -ge 2048) 'A chave pública possui tamanho insuficiente.'
        $taskSignature = ConvertFrom-Base64UrlE15 $taskParts[2]
        $taskSigned = [Text.Encoding]::ASCII.GetBytes($taskParts[0] + '.' + $taskParts[1])
        Assert-E15 ($taskRsa.VerifyData($taskSigned, $taskSignature, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)) 'A assinatura JWT não validou com a chave pública.'
    }
    finally { $taskRsa.Dispose() }
}

function Get-RabbitQueueE15 {
    $taskResponse = Invoke-HttpE15 -Uri ($taskRabbitUri + '/api/queues/%2F/notifications-user-created') -Headers $taskRabbitHeaders
    Assert-E15 ([int]$taskResponse.StatusCode -eq 200) 'A fila de demonstração não está disponível no RabbitMQ.'
    return ConvertFrom-JsonE15 $taskResponse.Content
}

function Wait-RabbitQueueE15 {
    $taskDeadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
    do {
        $taskQueue = Get-RabbitQueueE15
        if ([int]$taskQueue.messages -eq 2 -and [int]$taskQueue.messages_ready -eq 2) {
            Assert-E15 ($taskQueue.durable -eq $true -and $taskQueue.auto_delete -eq $false -and [int]$taskQueue.consumers -eq 0) 'A fila não preserva a configuração de demonstração esperada.'
            return $taskQueue
        }
        Start-Sleep -Milliseconds 750
    } while ([DateTimeOffset]::UtcNow -lt $taskDeadline)
    throw [InvalidOperationException]::new('E15: A fila não apresentou os dois eventos dentro do prazo.')
}

function Assert-RabbitEnvelopesE15 {
    param([hashtable]$Snapshot)
    $taskQueue = Wait-RabbitQueueE15
    $taskResponse = Invoke-HttpE15 -Uri ($taskRabbitUri + '/api/queues/%2F/notifications-user-created/get') -Method 'POST' -Headers $taskRabbitHeaders -Body @{ count = 10; ackmode = 'ack_requeue_true'; encoding = 'base64'; truncate = 50000 }
    Assert-E15 ([int]$taskResponse.StatusCode -eq 200) 'Não foi possível consultar os envelopes da fila.'
    $taskMessages = @(ConvertFrom-JsonE15 $taskResponse.Content)
    Assert-E15 ($taskMessages.Count -eq 2) 'A fila não retornou exatamente os dois envelopes esperados.'
    $taskSeen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($taskMessage in $taskMessages) {
        Assert-E15 ($taskMessage.payload_encoding -ceq 'base64' -and [int]$taskMessage.properties.delivery_mode -eq 2) 'O envelope não é uma mensagem persistente válida.'
        $taskEnvelope = ConvertFrom-JsonE15 ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($taskMessage.payload)))
        Assert-E15 (@($taskEnvelope.messageType) -ccontains $taskUrn) 'O envelope possui uma identidade de contrato diferente.'
        $taskPayload = $taskEnvelope.message
        $taskExpectedFields = @('eventId', 'correlationId', 'occurredAt', 'version', 'userId', 'name', 'email') | Sort-Object
        $taskActualFields = @($taskPayload.Keys) | Sort-Object
        Assert-E15 (($taskActualFields -join '|') -ceq ($taskExpectedFields -join '|')) 'O evento não possui exatamente os sete campos contratados.'
        $taskEvent = @($Snapshot.events | Where-Object { $_.eventId -eq $taskPayload.eventId })
        Assert-E15 ($taskEvent.Count -eq 1 -and $taskSeen.Add([string]$taskPayload.eventId)) 'A fila contém evento desconhecido ou duplicado nesta prova.'
        Assert-E15 ($taskEnvelope.messageId -eq $taskPayload.eventId -and $taskEnvelope.correlationId -eq $taskPayload.correlationId) 'O envelope não preservou as identidades do evento.'
        foreach ($taskField in $taskExpectedFields) {
            if ($taskField -ceq 'occurredAt') {
                Assert-E15 ([DateTimeOffset]::Parse([string]$taskPayload[$taskField]) -eq [DateTimeOffset]::Parse([string]$taskEvent[0].payload[$taskField])) 'O instante do evento divergiu da Outbox.'
            }
            else {
                Assert-E15 ($taskPayload[$taskField] -ceq $taskEvent[0].payload[$taskField]) ('O campo ' + $taskField + ' divergiu da Outbox.')
            }
        }
    }
    # ack_requeue_true consulta e recoloca cada mensagem; nenhuma mensagem é removida da fila.
    return [ordered]@{ mensagens = [int]$taskQueue.messages; eventos = @($taskSeen | Sort-Object); consulta = 'ack_requeue_true; mensagens preservadas' }
}

function Get-SafeDatabaseSummaryE15 {
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
    Assert-E15 ($LASTEXITCODE -eq 0) 'Não foi possível inventariar a fonte Git.'
    foreach ($taskRelative in $taskFiles) {
        if ($taskRelative -match '(^|/)(\.git|\.codex|\.agents|\.aws|\.local|secrets|bin|obj|TestResults|coverage)(/|$)' -or
            $taskRelative -match '(^|/)\.env($|\.)|\.(pem|key|pfx|p12|log|tmp)$|(^|/)(secrets\.json|appsettings\..*Local\.json)$') { continue }
        $taskOriginal = [IO.Path]::GetFullPath((Join-Path $taskRepo $taskRelative))
        $taskDestination = [IO.Path]::GetFullPath((Join-Path $taskSource $taskRelative))
        Assert-E15 ($taskOriginal.StartsWith($taskRepo + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -and
            $taskDestination.StartsWith($taskSource + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Um caminho de cópia ficou fora da área da prova.'
        if (-not (Test-Path -LiteralPath $taskOriginal -PathType Leaf)) { continue }
        $taskAncestor = $taskOriginal
        while ($taskAncestor -ne $taskRepo) {
            Assert-E15 (((Get-Item -LiteralPath $taskAncestor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'A cópia não segue links de arquivos ou diretórios.'
            $taskAncestor = Split-Path -Parent $taskAncestor
        }
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskDestination) -Force | Out-Null
        Copy-Item -LiteralPath $taskOriginal -Destination $taskDestination
        $taskManifest.Add([ordered]@{ arquivo = $taskRelative; sha256 = (Get-FileHash -LiteralPath $taskDestination -Algorithm SHA256).Hash })
    }
    Assert-E15 ((Test-Path -LiteralPath $taskComposeFile) -and (Test-Path -LiteralPath (Join-Path $taskSource 'Dockerfile'))) 'A cópia não possui Compose e Dockerfile.'
    $taskManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskResults 'manifesto-fonte-e15.json') -Encoding utf8
    Assert-E15 ($taskProject -match '^fcg-users-e15-[a-f0-9]{32}$') 'O projeto temporário possui nome inesperado.'
    $taskExisting = Invoke-DockerCaptureE15 -Arguments @('ps', '--all', '--filter', ('label=com.docker.compose.project=' + $taskProject), '--format', '{{.ID}}')
    Assert-E15 ([string]::IsNullOrWhiteSpace($taskExisting)) 'O projeto temporário já possui recursos; a prova não os reutilizará.'
    $taskHttpPort = Get-FreePortE15
    do { $taskRabbitPort = Get-FreePortE15 } while ($taskRabbitPort -eq $taskHttpPort)
    [Environment]::SetEnvironmentVariable('FCG_USERS_HTTP_PORT', [string]$taskHttpPort)
    [Environment]::SetEnvironmentVariable('FCG_RABBIT_MANAGEMENT_PORT', [string]$taskRabbitPort)
    $taskApiUri = 'http://127.0.0.1:' + $taskHttpPort
    $taskRabbitUri = 'http://127.0.0.1:' + $taskRabbitPort
    $taskRabbitHeaders = @{ Authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes('fcg_demo:Rabbit_Demo_2026!')) }
    $taskOwnProject = $true

    $taskStage = 'primeira inicialização Compose'
    Write-Host 'E15: iniciando uma cópia limpa com Docker Compose e volumes novos.'
    Invoke-ComposeVisibleE15 -Arguments @('up', '--build', '-d')
    Wait-ReadyE15
    $taskChecks.Add('Instalação nova: Compose, preparador exit 0, contêiner saudável, /health e Swagger HTTP 200.')

    $taskStage = 'cadastro, login e autorização'
    Write-Host 'E15: verificando cadastro, login, permissões e assinatura JWT.'
    $taskAdminResponse = Invoke-HttpE15 -Uri ($taskApiUri + '/api/v1/auth/login') -Method 'POST' -Body @{ email = 'admin@fcg.local'; senha = 'Admin_Demo_2026!' }
    Assert-E15 ([int]$taskAdminResponse.StatusCode -eq 200) 'O login do administrador sintético falhou.'
    $taskAdmin = ConvertFrom-JsonE15 $taskAdminResponse.Content
    Assert-JwtE15 -Token $taskAdmin.accessToken -UserId $taskAdmin.usuario.id -Role 'Administrador'
    $taskCorrelation = [guid]::NewGuid().ToString('D')
    $taskCreationResponse = Invoke-HttpE15 -Uri ($taskApiUri + '/api/v1/usuarios') -Method 'POST' -Headers @{ 'X-Correlation-ID' = $taskCorrelation } -Body @{
        nome = 'Pessoa Demonstração E15'; cpf = '11144477735'; dataNascimento = '2000-01-01'
        email = 'pessoa-e15@exemplo.test'; senha = 'Usuario_Demo_2026!'
    }
    Assert-E15 ([int]$taskCreationResponse.StatusCode -eq 201) 'O cadastro público sintético falhou.'
    Assert-E15 ([string]$taskCreationResponse.Headers['X-Correlation-ID'] -eq $taskCorrelation) 'A API não devolveu a correlação informada.'
    $taskUser = ConvertFrom-JsonE15 $taskCreationResponse.Content
    $taskLoginResponse = Invoke-HttpE15 -Uri ($taskApiUri + '/api/v1/auth/login') -Method 'POST' -Body @{ email = 'pessoa-e15@exemplo.test'; senha = 'Usuario_Demo_2026!' }
    Assert-E15 ([int]$taskLoginResponse.StatusCode -eq 200) 'O login do usuário sintético falhou.'
    $taskLogin = ConvertFrom-JsonE15 $taskLoginResponse.Content
    Assert-JwtE15 -Token $taskLogin.accessToken -UserId $taskUser.id -Role 'Usuario'
    $taskUserHeaders = @{ Authorization = 'Bearer ' + $taskLogin.accessToken }
    $taskAdminHeaders = @{ Authorization = 'Bearer ' + $taskAdmin.accessToken }
    Assert-E15 ([int](Invoke-HttpE15 -Uri ($taskApiUri + '/api/v1/usuarios/' + $taskUser.id)).StatusCode -eq 401) 'Uma consulta sem token não foi recusada.'
    Assert-E15 ([int](Invoke-HttpE15 -Uri ($taskApiUri + '/api/v1/usuarios/' + $taskUser.id) -Headers $taskUserHeaders).StatusCode -eq 200) 'O usuário não conseguiu consultar seu cadastro.'
    Assert-E15 ([int](Invoke-HttpE15 -Uri ($taskApiUri + '/api/v1/usuarios/' + $taskUser.id + '/perfil') -Method 'PUT' -Headers $taskUserHeaders -Body @{ perfilId = '22222222-2222-2222-2222-222222222222' }).StatusCode -eq 403) 'O usuário comum conseguiu acessar uma ação administrativa.'
    Assert-E15 ([int](Invoke-HttpE15 -Uri ($taskApiUri + '/api/v1/usuarios/' + $taskUser.id) -Headers $taskAdminHeaders).StatusCode -eq 200) 'O administrador não conseguiu consultar o usuário.'
    $taskChecks.Add('Cadastro HTTP 201; logins HTTP 200; acesso anônimo 401, titular 200, ação administrativa por usuário comum 403 e administrador 200.')
    $taskChecks.Add('JWT dos dois perfis validado com apenas a chave pública: RSA >= 2048, RS256, kid, sub, role, issuer, audience e exp.')

    $taskStage = 'Outbox, fila e isolamento das chaves'
    Write-Host 'E15: comparando banco, eventos publicados e mensagens persistentes no RabbitMQ.'
    $taskBefore = Wait-PublishedE15
    Assert-DatabaseSnapshotE15 $taskBefore
    $taskCreatedEvent = @($taskBefore.events | Where-Object { $_.userId -eq $taskUser.id })
    Assert-E15 ($taskCreatedEvent.Count -eq 1 -and $taskCreatedEvent[0].correlationId -eq $taskCorrelation) 'A Outbox não preservou a correlação do cadastro público.'
    $taskQueueBefore = Assert-RabbitEnvelopesE15 $taskBefore
    $taskHashesBefore = Get-KeyFingerprintsE15
    $taskApiContainer = Get-ServiceIdE15 'usersapi'
    $taskMounts = @(ConvertFrom-JsonE15 (Invoke-DockerCaptureE15 -Arguments @('inspect', '--format', '{{json .Mounts}}', $taskApiContainer)))
    $taskJwtMount = @($taskMounts | Where-Object { $_.Destination -ceq '/data/jwt' })
    Assert-E15 ($taskJwtMount.Count -eq 1 -and $taskJwtMount[0].RW -eq $false -and $taskJwtMount[0].Type -ceq 'volume') 'A API não recebeu o volume JWT somente para leitura.'
    $taskUid = Invoke-ComposeCaptureE15 -Arguments @('exec', '-T', 'usersapi', 'id', '-u')
    Assert-E15 ($taskUid -ceq '1654') 'A API não está executando com o usuário não privilegiado esperado.'
    $taskChecks.Add('Duas migrations, dois usuários, um administrador, duas auditorias e dois eventos publicados; IDs da Outbox e dos envelopes iguais, com os sete campos do contrato e URN esperado.')
    $taskChecks.Add('Fila durável e mensagens persistentes; consulta ack_requeue_true preserva as mensagens, sem simular consumo ou email.')
    $taskChecks.Add('API executa como UID 1654 e recebe as chaves em volume somente para leitura; somente fingerprints são registrados.')

    $taskStage = 'reinício preservando volumes'
    Write-Host 'E15: recriando os contêineres e conferindo preservação dos dados, chaves e mensagens.'
    [void](Invoke-ComposeCaptureE15 -Arguments @('down')) # Sem --volumes: esta etapa precisa preservar os dados.
    Invoke-ComposeVisibleE15 -Arguments @('up', '--build', '-d')
    Wait-ReadyE15
    $taskAfter = Wait-PublishedE15
    Assert-DatabaseSnapshotE15 $taskAfter
    $taskHashesAfter = Get-KeyFingerprintsE15
    Assert-E15 ($taskHashesBefore.private -ceq $taskHashesAfter.private -and $taskHashesBefore.public -ceq $taskHashesAfter.public) 'O reinício alterou o par de chaves.'
    $taskSafeBefore = Get-SafeDatabaseSummaryE15 $taskBefore
    $taskSafeAfter = Get-SafeDatabaseSummaryE15 $taskAfter
    Assert-E15 (($taskSafeBefore | ConvertTo-Json -Depth 8 -Compress) -ceq ($taskSafeAfter | ConvertTo-Json -Depth 8 -Compress)) 'O reinício alterou ou duplicou usuários, auditorias ou eventos.'
    $taskQueueAfter = Assert-RabbitEnvelopesE15 $taskAfter
    Assert-E15 (($taskQueueBefore.eventos -join '|') -ceq ($taskQueueAfter.eventos -join '|')) 'O reinício alterou os eventos presentes na fila.'
    Assert-E15 ([int](Invoke-HttpE15 -Uri ($taskApiUri + '/api/v1/usuarios/' + $taskUser.id) -Headers $taskUserHeaders).StatusCode -eq 200) 'O JWT emitido antes do reinício deixou de validar.'
    $taskAdminAgainResponse = Invoke-HttpE15 -Uri ($taskApiUri + '/api/v1/auth/login') -Method 'POST' -Body @{ email = 'admin@fcg.local'; senha = 'Admin_Demo_2026!' }
    Assert-E15 ([int]$taskAdminAgainResponse.StatusCode -eq 200) 'O administrador não conseguiu entrar após o reinício.'
    $taskAdminAgain = ConvertFrom-JsonE15 $taskAdminAgainResponse.Content
    Assert-E15 ($taskAdminAgain.usuario.id -eq $taskAdmin.usuario.id) 'O administrador foi substituído no reinício.'
    $taskChecks.Add('Compose down sem remover volumes + up --build -d: mesmos usuários, auditorias, eventos, tentativas e hashes; mesma conta administrativa e JWT antigo ainda aceito.')
    $taskChecks.Add('RabbitMQ preserva os mesmos dois eventos após recriação dos contêineres; nenhuma mensagem adicional foi criada pelo preparador.')

    $taskStage = 'recusas do preparador'
    Write-Host 'E15: verificando que a preparação recusa ambiente Production e flag de demonstração desabilitada.'
    foreach ($taskOverride in @('DOTNET_ENVIRONMENT=Production', 'DemonstracaoLocal__Habilitada=false')) {
        $taskRejectedOutput = @(& docker @taskComposeArguments run --rm --no-deps --env $taskOverride preparacao 2>&1)
        $taskRejectedExit = $LASTEXITCODE
        Assert-E15 ($taskRejectedExit -eq 1) 'O preparador não recusou uma configuração fora da demonstração.'
        # O conteúdo do subprocesso não entra no relatório; somente o código de saída é evidência.
        $taskRejectedOutput = $null
    }
    $taskFinal = Get-DatabaseSnapshotE15
    Assert-DatabaseSnapshotE15 $taskFinal
    Assert-E15 (((Get-SafeDatabaseSummaryE15 $taskFinal) | ConvertTo-Json -Depth 8 -Compress) -ceq ($taskSafeAfter | ConvertTo-Json -Depth 8 -Compress)) 'Uma execução recusada alterou registros.'
    $taskFinalHashes = Get-KeyFingerprintsE15
    Assert-E15 ($taskFinalHashes.private -ceq $taskHashesAfter.private -and $taskFinalHashes.public -ceq $taskHashesAfter.public) 'Uma execução recusada alterou as chaves.'
    $taskChecks.Add('Preparador em Production ou com DemonstracaoLocal desabilitada retorna exit 1; registros e chaves permanecem intactos.')

    $taskImages = @('postgres', 'rabbitmq', 'preparacao', 'usersapi') | ForEach-Object {
        $taskService = $_
        $taskImage = Invoke-DockerCaptureE15 -Arguments @('inspect', '--format', '{{.Image}}', (Get-ServiceIdE15 $taskService))
        [ordered]@{ servico = $taskService; imagemId = $taskImage }
    }
    $taskSummary = [ordered]@{
        entrega = 'E15'; resultado = 'aprovado'; inicioUtc = $taskStarted.ToString('o')
        fonte = 'Cópia da working tree atual via git ls-files; sem .git, .env, chaves, bin, obj ou TestResults. Não é clone de um commit publicado.'
        projetoDescartavel = $taskProject; arquivosFonte = $taskManifest.Count; manifesto = $taskManifest.ToArray()
        comando = 'docker compose up --build -d, com projeto e arquivo da cópia temporária e portas livres.'
        verificacoesAprovadas = $taskChecks.ToArray()
        bancoAntesReinicio = $taskSafeBefore; bancoDepoisReinicio = $taskSafeAfter
        chaves = [ordered]@{ antes = $taskHashesBefore; depois = $taskHashesAfter; privadaLidaOuImpressa = $false; volumeApiSomenteLeitura = $true; usuarioApiUid = 1654 }
        fila = [ordered]@{ antes = $taskQueueBefore; depois = $taskQueueAfter; consumidorNotificationsExecutado = $false; emailEnviado = $false }
        reinicializacoes = 1; preparacaoInicialExit = 0; preparacaoReinicioExit = 0
        recusasPreparador = @([ordered]@{ ambiente = 'Production'; exit = 1 }, [ordered]@{ demonstracaoHabilitada = $false; exit = 1 })
        imagens = @($taskImages)
    }
}
catch {
    $taskException = $_.Exception
    $taskDetail = if ($taskException.Message.StartsWith('E15:', [StringComparison]::Ordinal)) { $taskException.Message } else { $taskException.GetType().Name }
    $taskFailure = 'A verificação E15 falhou durante ' + $taskStage + '. ' + $taskDetail
}
finally {
    try {
        if ($taskOwnProject) {
            # Somente o projeto aleatório desta execução. A pasta de fontes e os resultados são preservados.
            Assert-E15 ($taskProject -match '^fcg-users-e15-[a-f0-9]{32}$' -and $taskComposeFile.StartsWith($taskSource + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'O alvo da limpeza descartável não foi confirmado.'
            [void](Invoke-ComposeCaptureE15 -Arguments @('down', '--volumes', '--remove-orphans'))
        }
    }
    catch { $taskFailure = 'A verificação E15 não conseguiu concluir a limpeza do projeto descartável ' + $taskProject + '.' }
    finally {
        [Environment]::SetEnvironmentVariable('FCG_USERS_HTTP_PORT', $taskOldHttpPort)
        [Environment]::SetEnvironmentVariable('FCG_RABBIT_MANAGEMENT_PORT', $taskOldRabbitPort)
        Pop-Location
    }
}

if ($null -ne $taskFailure) { throw [InvalidOperationException]::new($taskFailure) }
Assert-E15 ($null -ne $taskSummary) 'A execução terminou sem uma evidência completa.'
$taskSummary.fimUtc = [DateTimeOffset]::UtcNow.ToString('o')
$taskSummary.limpeza = 'Contêineres, rede e volumes somente do projeto descartável removidos; fonte e evidências locais preservadas.'
$taskReport = Join-Path $taskResults 'demonstracao-e15.json'
$taskSummary | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $taskReport -Encoding utf8
Write-Host 'E15: todas as verificações aprovadas e recursos descartáveis removidos. Relatório: TestResults/E15/demonstracao-e15.json.'

