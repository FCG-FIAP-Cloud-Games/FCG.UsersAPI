# FCG.Users.Application

Camada dos casos de uso de usuários e autenticação. Depende somente de Domain.

Na E04, recebeu comandos, resultados e manipuladores de cadastro, consulta, atualização, alteração de perfil, login, renovação e logout, além das abstrações de repositórios e segurança.

- `Usuarios/`: fluxos e dados de usuários.
- `Auth/`: coordenação de login, refresh e logout.
- `Abstractions/Repositories/`: operações de persistência necessárias; ainda sem implementação no novo serviço.
- `Abstractions/Security/`: contratos de hash e emissão de tokens; ainda sem criptografia concreta.
- `NormalizadorIdentidade.cs`: normalização usada pela autenticação.

Os corpos dos métodos e contratos da origem foram preservados. O arquivo IRepositoryUsuarios.cs corresponde ao nome do tipo que já existia no monólito. A assinatura JWT RS256, o banco e a composição no host entram nas próximas entregas.

Os testes usam substitutos definidos exclusivamente no projeto UnitTests. Nenhuma implementação em memória foi registrada na API. Autorizações por titularidade/role e perfil padrão do cadastro público serão conectados na borda HTTP, seguindo o comportamento de origem.

Leia o [capítulo da E04](../../docs/aprendizado/E04-DOMINIO-E-CASOS-DE-USO.md) e o [mapa de extração](../../docs/planejamento/MAPA-EXTRACAO-IDENTITY-E04.md).
