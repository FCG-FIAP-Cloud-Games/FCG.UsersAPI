# Testes unitários do UsersAPI

xUnit 3, Domain/Application. E14: em cópia limpa, os 109 casos foram reexecutados e aprovados, zero falhas/ignorados. A nova cobertura técnica do publicador fica em IntegrationTests.

| Grupo | Verifica |
|---|---|
| Domain | Usuário/perfil, CPF/nascimento, token e quatro ações de auditoria. |
| Usuarios | Validações, conflitos, normalização, consulta/administração e evento de cadastro. |
| Auth | Coordenação de login/refresh/logout com substitutos. |
| Support | Relógios e unidades de trabalho substitutos. |

Três casos novos sobre a base de 106: correlação informada preservada; correlação vazia rejeitada antes da persistência; precisão de microssegundos idêntica entre usuário/log/evento. Casos existentes passaram a capturar o evento e conferir sua ausência em cadastros recusados.

Substitutos não demonstram transação real. Atomicidade, rollback/commit e concorrência são comprovados no projeto IntegrationTests com PostgreSQL.

Na raiz, após compilar Release:

```powershell
dotnet test tests/FCG.Users.UnitTests -c Release --no-build --no-restore
```

Não exige Docker. [Guia E11](../../docs/aprendizado/E11-CADASTRO-E-OUTBOX-ATOMICA.md) e [evidências](../../docs/evidencias/E11/README.md).

[Evidências atuais E14](../../docs/evidencias/E14/README.md).
