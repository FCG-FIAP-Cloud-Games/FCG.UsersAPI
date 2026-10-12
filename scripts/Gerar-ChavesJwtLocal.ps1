#requires -Version 7.4
[CmdletBinding()]
param([ValidatePattern('^[A-Za-z0-9_-]{1,64}$')][string]$KeyId = 'users-local-v1')

$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
$taskFolder = Join-Path $taskRepo ('.local/jwt/' + $KeyId)
$taskPrivate = Join-Path $taskFolder 'private.pem'
$taskPublic = Join-Path $taskFolder 'public.pem'
if (Test-Path -LiteralPath $taskFolder) { throw 'A pasta de chaves já existe; nada foi substituído. Use o par existente ou escolha outro KeyId.' }
New-Item -ItemType Directory -Path $taskFolder | Out-Null
$taskRsa = [Security.Cryptography.RSA]::Create(2048)
try {
    # CreateNew evita substituir arquivos, mesmo em concorrência. Nenhum conteúdo de chave é impresso.
    foreach ($taskItem in @(@{ Path = $taskPrivate; Pem = $taskRsa.ExportPkcs8PrivateKeyPem() }, @{ Path = $taskPublic; Pem = $taskRsa.ExportSubjectPublicKeyInfoPem() })) {
        $taskStream = [IO.File]::Open($taskItem.Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try { $taskBytes = [Text.Encoding]::UTF8.GetBytes($taskItem.Pem); $taskStream.Write($taskBytes) }
        finally { $taskStream.Dispose() }
    }
}
finally { $taskRsa.Dispose() }
Write-Host ('Par RSA local criado em .local/jwt/' + $KeyId + '. Configure a API conforme docs/operacao/EXECUTAR-USERSAPI-DO-ZERO.md.')
