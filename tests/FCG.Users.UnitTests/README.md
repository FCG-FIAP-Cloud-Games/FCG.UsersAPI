# Testes unitários do UsersAPI

xUnit 3, Domain/Application. **106 aprovados em 06/10/2026**, zero falhas/ignorados.

| Grupo | Verifica |
|---|---|
| Domain | Usuário/perfil, CPF/nascimento, token e quatro descrições de auditoria. |
| Usuarios | Cadastro/consulta/dados/perfil/inativação; validações, conflitos, auditoria e idempotência. |
| Auth | Coordenação de login/refresh/logout com substitutos. |
| Support | Unidade de trabalho substituta, apenas para coordenação dos casos de uso. |

Seis novos cenários de inativação verificam usuário ativo/registro do mesmo alvo e instante, repetição sem log/data nova, GUID vazio, ausente e conflitos inesperados. Os testes de dados/perfil passaram a conferir a auditoria entregue ao repositório.

Relógios controlados comprovam os instantes sem depender da data da execução. Substitutos não simulam a garantia de banco: transações, lock, atomicidade e concorrência são verificados no projeto IntegrationTests com PostgreSQL real.

Após compilar, na raiz:

```powershell
dotnet test tests/FCG.Users.UnitTests -c Release --no-build --no-restore
```

Não exige Docker. [Guia E09](../../docs/aprendizado/E09-OPERACOES-PROTEGIDAS-E-AUDITORIA.md) e [evidências](../../docs/evidencias/E09/README.md).
