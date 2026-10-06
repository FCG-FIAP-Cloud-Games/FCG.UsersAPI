# Testes unitários do UsersAPI

Projeto xUnit 3, com Domain e Application. Revalidados na conclusão da E08, em 05/10/2026: **100 testes aprovados**.

| Pasta | Conteúdo |
|---|---|
| Domain | Usuário/perfil, CPF, nascimento civil, ciclo de token e quatro ações de auditoria. |
| Usuarios | Cadastro, consulta, atualização, troca de perfil, limites, conflitos e coordenação da auditoria do cadastro. |
| Auth | Coordenação de login, renovação e logout com dependências substitutas. |

Os substitutos privados ficam junto dos cenários. Relógios fixos permitem verificar expiração e nascimento futuro sem depender da data da máquina. Dados inválidos são rejeitados antes de consultar o repositório ou calcular hash.

Na E06, o repositório substituto recebe o par Usuario/LogUsuario. Os testes verificam que a aplicação envia a auditoria do usuário correto. Um relógio que avança a cada consulta comprova que cadastro e log recebem o mesmo instante capturado pelo caso de uso, sem depender de duas leituras coincidentes do relógio.

Na raiz, após compilar:

```powershell
dotnet test tests/FCG.Users.UnitTests --configuration Release --no-build --no-restore
```

Não exige Docker. Esses testes comprovam regras e coordenação. O projeto IntegrationTests verifica o hash concreto, o cadastro HTTP, as transações e a concorrência no PostgreSQL. Na E07, o projeto IntegrationTests também comprova login HTTP com PostgreSQL, JWT/RS256, configuração das chaves e autorização 401/403 usando controllers exclusivos de teste.

Consulte o [guia E07](../../docs/aprendizado/E07-LOGIN-E-JWT-RS256.md) e as [evidências E07](../../docs/evidencias/E07/README.md). O [guia E06](../../docs/aprendizado/E06-CADASTRO-HTTP-E-AUDITORIA.md) e as [evidências](../../docs/evidencias/E06/README.md). O [guia E05](../../docs/aprendizado/E05-PERSISTENCIA-PROPRIA.md) preserva a explicação dos testes de persistência.

E08: os mesmos 100 unitários reexecutados/aprovados. Comprovação HTTP/concorrência está nos 24 cenários reais de TestesSessaoHttp, sem repetir banco em novos mocks. Veja [guia E08](../../docs/aprendizado/E08-REFRESH-LOGOUT-E-CONCORRENCIA.md) e [evidências](../../docs/evidencias/E08/README.md).
