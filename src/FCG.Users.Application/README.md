# FCG.Users.Application

Casos de uso de usuários/autenticação. Depende somente de Domain; declara interfaces sem ASP.NET Core, EF ou MassTransit. E11 adiciona o contrato de evento de cadastro.

- Usuarios: cadastro, consulta, atualização, troca de perfil e inativação.
- Auth: login, refresh e logout.
- Abstractions/Repositories: usuários, tokens e unidade de trabalho.
- Abstractions/Security: hash/JWT/refresh.
- Messaging/Contracts: definição única de UserCreatedEvent.
- Messaging/DefinicaoUserCreatedEvent: versão e identidade de integração.

ManipuladorCriarUsuario normaliza/valida a entrada e prepara usuário, auditoria e evento com o mesmo instante UTC em microssegundos. CorrelationId é opcional no comando; EventId é gerado uma vez. IRepositoryUsuarios.TentarAdicionarAsync exige os três objetos, confirmados pela implementação de persistência.

O contrato conserva FCG.Notifications.Application.Messaging.Contracts como namespace de integração, sem referência ao projeto Notifications. A ferramenta de prova E10 referencia este projeto para usar a mesma definição.

Atualização/perfil/inativação usam IUnidadeDeTrabalhoUsuarios e não geram UserCreatedEvent. Dados/perfil idênticos ou conta já inativa não geram nova auditoria. Login/refresh/logout preservam a coordenação por usuário implementada nas entregas anteriores.

Application não publica mensagens nem conhece a tabela de Outbox. Infrastructure implementa armazenamento e, na E12, transporte/processamento técnico. Autenticação/autorização HTTP ficam na Api.

[Guia E11](../../docs/aprendizado/E11-CADASTRO-E-OUTBOX-ATOMICA.md) e [contrato](../../docs/contratos/CONTRATO-USERCREATED-E11.md).
