# FCG.Users.Api

Host ASP.NET Core e entrada HTTP do UsersAPI.

Na E03, contém controllers, tratamento de erros com Problem Details, Swagger em desenvolvimento e `GET /health`. O health confirma somente que o processo responde; não consulta banco ou RabbitMQ.

- `Program.cs`: registra os serviços e monta o pipeline HTTP.
- `Configuration/`: configuração de Problem Details e Swagger.
- `Controllers/HealthController.cs`: recebe a chamada de saúde e usa `HealthCheckService` via injeção de dependência.
- `Contracts/HealthResponse.cs`: formato da resposta de saúde.
- `Properties/launchSettings.json`: perfil `http`, ambiente Development, porta 5088.
- `FCG.Users.Api.http`: requisições para experimentação local.

Referencia Application e Infrastructure. Os controllers de negócio, nas próximas entregas, chamarão os casos de uso; as regras não serão implementadas no transporte HTTP.

