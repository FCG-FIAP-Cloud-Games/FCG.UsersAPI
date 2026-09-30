# Testes unitários do UsersAPI

Projeto xUnit 3, referenciando Domain e Application. A E04 possui 68 casos aprovados.

| Pasta | Conteúdo |
|---|---|
| Domain | Invariantes de usuário/perfil e ciclo de vida do registro de refresh token. |
| Usuarios | Cadastro, consulta, atualização e troca de perfil. |
| Auth | Coordenação de login, renovação e logout com dependências substitutas. |

Sete arquivos de teste foram extraídos/adaptados da origem e um foi criado para domínio. Os substitutos privados ficam junto dos cenários. Relógios fixos tornam a verificação de expiração independente do horário da máquina.

Na raiz do repositório, após o build:

```powershell
dotnet test tests/FCG.Users.UnitTests/FCG.Users.UnitTests.csproj --configuration Release --no-build --no-restore
```

Para estudar somente renovação/logout, acrescente `--filter 'FullyQualifiedName~TestesRenovacaoELogout'`.

Os emissores retornam valores fictícios e os repositórios não usam PostgreSQL. Estes testes verificam domínio e coordenação, sem comprovar criptografia, rotação concorrente no banco ou autorização HTTP. Os objetos de teste não são utilizados pelo host da API.

Consulte o [capítulo da E04](../../docs/aprendizado/E04-DOMINIO-E-CASOS-DE-USO.md) e as [evidências](../../docs/evidencias/E04/README.md).
