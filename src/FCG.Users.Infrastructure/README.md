# FCG.Users.Infrastructure

PostgreSQL próprio e segurança concreta do UsersAPI. E11 acrescenta Outbox própria e segunda migration. E12 acrescenta transporte/worker sem alterar o schema.

| Grupo | Responsabilidade |
|---|---|
| Data/UsersDbContext | Usuario, Perfil, Token, LogUsuario e MensagemOutbox. |
| Data/Outbox | Estado técnico e serialização do snapshot de UserCreatedEvent. |
| Data/Mappings e Migrations | Schema, índices/checks/FKs e migrations próprias. |
| Data/UsersDbContextFactory | Ferramenta EF via ConnectionStrings__UsersDatabase. |
| RepositorioUsuarios | Cadastro com usuário/auditoria/evento numa gravação; atualização com auditoria. |
| UnidadeDeTrabalhoUsuarios | Transação e SELECT FOR UPDATE parametrizado por usuário. |
| RepositorioTokens | Hash de refresh, rotação condicional e logout coordenados. |
| InicializadorAdministradorLocal | Primeiro administrador explícito, transação e lock do perfil. |
| Security | Hash compatível, RSA externo/kid, emissão JWT e refresh aleatório. |
| Messaging | Configuração externa, transporte MassTransit/RabbitMQ, processador e BackgroundService. |
| DependencyInjection | Registros e validação de configuração. |

Cadastro valida coerência dos três objetos, inclui-os no contexto e chama SaveChangesAsync uma vez. Falhas desanexam a tentativa rejeitada; somente constraints conhecidas de e-mail/CPF são traduzidas em conflito de cadastro. EventId duplicado é falha técnica.

tb_Outbox guarda nove campos, payload jsonb de sete propriedades, UTC, EventId único, FK Restrict, checks e índice parcial para pendências. Nasce com PublishedAt nulo e Attempts zero. Na E12, o worker opcional reserva cada linha com FOR UPDATE SKIP LOCKED, valida o snapshot e publica com confirmação/mandatory. Persiste Attempts e PublishedAt após a tentativa; falhas mantêm a pendência. Não usa lease ou Outbox nativa do MassTransit.

Migração inicial: 20261004145013_CriacaoInicialUsers. Evolução E11: 20261007023803_OutboxUserCreated. Sem migrations automáticas no startup ou eventos retroativos para usuários existentes.

AtualizarAsync exige usuário rastreado e auditoria do mesmo alvo. Grava somente campos alterados, sem recriar o evento de cadastro. Operações protegidas/login/refresh/logout preservam a unidade de trabalho por usuário; somente a transação externa confirma os fluxos que a utilizam.

Hash: PBKDF2-SHA256 com salt aleatório, verificação compatível com os formatos aprovados. Refresh: aleatório, SHA-256 no banco e duração configurável. JWT: RS256, privadas externas e públicas selecionadas por kid confiável. As regras atuais de sessão foram revalidadas na suíte completa E13.

[Guia E11](../../docs/aprendizado/E11-CADASTRO-E-OUTBOX-ATOMICA.md) · [Decisão](../../docs/decisoes/ADR-005-OUTBOX-PROPRIA-E-CADASTRO-ATOMICO.md) · [SQL da evolução](../../docs/evidencias/E11/outbox-migration.sql).

E12 usa lotes sequenciais, prazo de publicação, cancelamento e espera progressiva geral após o lote. Nenhum agendamento por linha foi acrescentado. Reenvio após envio sem commit é possível; identidade/payload são conservados para deduplicação no consumidor.

[Guia E12](../../docs/aprendizado/E12-PUBLICADOR-OUTBOX-E-RABBITMQ.md) · [Configuração](../../docs/contratos/CONFIGURACAO-PUBLICADOR-E12.md) · [Evidências atuais](../../docs/evidencias/E12/README.md).


E13 comprovou o mesmo publicador em processos reais: retorno do broker, reinício com pendência, crash após envio e duas instâncias. O código de produção não foi alterado. [Provas E13](../../docs/evidencias/E13/README.md) · [Guia E13](../../docs/aprendizado/E13-RECUPERACAO-REENTREGA-E-CONCORRENCIA.md).
