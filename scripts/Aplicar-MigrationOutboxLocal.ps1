#requires -Version 7.4
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path -Parent $PSScriptRoot
Push-Location $taskRepo
try {
    & (Join-Path $PSScriptRoot 'Configurar-AmbienteE11Local.ps1') -SemJwt
    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar a ferramenta EF.' }
    # Sem --no-build: aplica o modelo atual, mesmo após editar os fontes.
    & dotnet tool run dotnet-ef database update --project src/FCG.Users.Infrastructure --startup-project src/FCG.Users.Infrastructure --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao aplicar a migration da Outbox.' }
    Write-Host 'Migrations do UsersAPI aplicadas. Nenhuma API, fila ou publicador foi iniciado.'
}
finally { Pop-Location }
