# Testes de integração do UsersAPI

Projeto xUnit 3 com `Microsoft.AspNetCore.Mvc.Testing`, referenciando Api e Infrastructure.

Na E03, possui **15 casos HTTP**, em `Host/TestesHostHttp.cs`. Eles exercitam o host da aplicação com `WebApplicationFactory<Program>`: roteamento, serialização, validação, middleware de erros e geração do Swagger.

`Support/FabricaUsersApi.cs` permite escolher o ambiente, substituir a configuração do Swagger e incluir opcionalmente o controller de cenários de teste. `Support/CenariosHttpController.cs` existe somente neste assembly de testes; suas rotas provocam falhas/validações sem adicionar endpoints de simulação ao projeto de produção.

Depois do build, execute a partir da raiz do repositório:

```powershell
dotnet test tests/FCG.Users.IntegrationTests/FCG.Users.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

O host é executado em memória. Não é preciso iniciar a API no terminal, abrir uma porta, disponibilizar banco ou RabbitMQ. Os testes não comprovam persistência ou mensageria; testes reais dessas dependências entram nas entregas correspondentes.

Consulte a [explicação dos testes](../../docs/aprendizado/E03-HOST-HTTP-SWAGGER-E-HEALTH.md#10-como-os-testes-verificam-o-host) e as [evidências obtidas](../../docs/evidencias/E03/README.md).
