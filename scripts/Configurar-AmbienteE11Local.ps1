#requires -Version 7.4
[CmdletBinding()]
param([switch]$SemJwt)

$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskEnvPath = Join-Path $taskRepo '.env.usersdb'
if (-not (Test-Path -LiteralPath $taskEnvPath)) { throw 'Arquivo .env.usersdb ausente. Este roteiro reutiliza o PostgreSQL local já preparado nas entregas anteriores.' }
$taskValues = @{}
foreach ($taskLine in Get-Content -LiteralPath $taskEnvPath) {
    if ($taskLine -match '^(POSTGRES_USER|POSTGRES_DB|POSTGRES_PASSWORD)=(.*)$') { $taskValues[$Matches[1]] = $Matches[2] }
}
foreach ($taskKey in @('POSTGRES_USER','POSTGRES_DB','POSTGRES_PASSWORD')) {
    if ([string]::IsNullOrWhiteSpace($taskValues[$taskKey])) { throw 'O arquivo de configuração do PostgreSQL está incompleto.' }
}
$taskPortsJson = & docker inspect --format '{{json .HostConfig.PortBindings}}' fcg-usersapi-postgres
if ($LASTEXITCODE -ne 0) { throw 'Container fcg-usersapi-postgres ausente. Não será criado ou substituído automaticamente.' }
$taskPorts = $taskPortsJson | ConvertFrom-Json
$taskBinding = @($taskPorts.'5432/tcp')
if ($taskBinding.Count -ne 1 -or $taskBinding[0].HostIp -ne '127.0.0.1' -or $taskBinding[0].HostPort -ne '55432') {
    throw 'O container esperado deve expor somente 127.0.0.1:55432 para PostgreSQL.'
}
$taskRunning = & docker inspect --format '{{.State.Running}}' fcg-usersapi-postgres
if ($LASTEXITCODE -ne 0) { throw 'Falha ao consultar o PostgreSQL local.' }
if ($taskRunning -ne 'true') {
    & docker start fcg-usersapi-postgres | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Não foi possível iniciar o PostgreSQL local.' }
}
$taskReady = $false
for ($taskAttempt = 0; $taskAttempt -lt 60; $taskAttempt++) {
    & docker exec fcg-usersapi-postgres pg_isready -U $taskValues['POSTGRES_USER'] -d $taskValues['POSTGRES_DB'] *> $null
    if ($LASTEXITCODE -eq 0) { $taskReady = $true; break }
    Start-Sleep -Milliseconds 500
}
if (-not $taskReady) { throw 'O PostgreSQL local não ficou pronto em 30 segundos.' }

# O builder protege caracteres especiais da senha; nenhuma avaliação de código do .env.
$taskConnection = [System.Data.Common.DbConnectionStringBuilder]::new()
$taskConnection['Host'] = '127.0.0.1'
$taskConnection['Port'] = '55432'
$taskConnection['Database'] = $taskValues['POSTGRES_DB']
$taskConnection['Username'] = $taskValues['POSTGRES_USER']
$taskConnection['Password'] = $taskValues['POSTGRES_PASSWORD']
$taskConnection['Include Error Detail'] = 'false'
$env:ConnectionStrings__UsersDatabase = $taskConnection.ConnectionString

if (-not $SemJwt) {
    $taskKeyDirectory = Join-Path $env:LOCALAPPDATA 'FCG.UsersAPI/jwt/users-local-v1'
    $taskPrivate = Join-Path $taskKeyDirectory 'private.pem'
    $taskPublic = Join-Path $taskKeyDirectory 'public.pem'
    if (-not (Test-Path -LiteralPath $taskPrivate) -or -not (Test-Path -LiteralPath $taskPublic)) {
        throw 'O par RSA local existente não foi encontrado. Este roteiro não gera nem substitui chaves.'
    }
    $env:Jwt__Signing__ActiveKeyId = 'users-local-v1'
    $env:Jwt__Signing__PrivateKeyPath = $taskPrivate
    $env:Jwt__Validation__Keys__0__KeyId = 'users-local-v1'
    $env:Jwt__Validation__Keys__0__PublicKeyPath = $taskPublic
}
Write-Host 'Ambiente E11 configurado nesta sessão; PostgreSQL próprio em 127.0.0.1:55432.'
