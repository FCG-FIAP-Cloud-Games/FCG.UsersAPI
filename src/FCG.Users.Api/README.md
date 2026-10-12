# FCG.Users.Api

Host ASP.NET Core do UsersAPI. Rotas e execução no [README principal](../../README.md).

- Saúde, cadastro público, login e refresh são públicos.
- Logout exige Bearer e usa sub como dono das sessões.
- Consulta/edição exigem titular ou Administrador.
- Cadastro administrativo, troca de perfil e inativação exigem Administrador.
- Cadastro público impõe Usuario; administrativo impõe Administrador.
- Nos dois cadastros, X-Correlation-ID opcional é validado, devolvido e enviado ao caso de uso.

E11: cadastros confirmam usuário/auditoria/Outbox juntos. O controller/caso de uso não publica. Na E12, Program registra transporte e worker opcionais, separados da requisição. Cabeçalho inválido é 400 antes do caso de uso; ausência gera correlação. Esse tratamento não é um middleware global nem idempotência.

DTO de usuário preserva sete campos seguros. JWT é validado antes das policies, sub/role preservados e kid confiável. Controllers usam no-store/no-cache; erros inesperados são 500 genéricos em Problem Details. Swagger somente em Development com flag habilitada.

Program preserva --inicializar-admin-local, restrito a Development/banco loopback. Esse ramo não inicia HTTP nem exige RSA. Uma criação nova passa pelo mesmo manipulador e grava evento; administrador existente não gera evento retroativo.

Sem migrations automáticas ou geração silenciosa de RSA. Access anterior conserva claims até expiração +30s, conforme contrato existente.

[Guia E11](../../docs/aprendizado/E11-CADASTRO-E-OUTBOX-ATOMICA.md) · [Evidências](../../docs/evidencias/E11/README.md) · [Exemplos HTTP](FCG.Users.Api.http).


E12: publicador desabilitado por padrão; broker inacessível não bloqueia abertura do host/cadastro quando as demais configurações são válidas. /health seleciona somente checks live e indica vida do processo, não prontidão do broker. O ramo CLI retorna antes de registrar MassTransit/worker.

[Guia E12](../../docs/aprendizado/E12-PUBLICADOR-OUTBOX-E-RABBITMQ.md) · [Configuração](../../docs/contratos/CONFIGURACAO-PUBLICADOR-E12.md) · [Evidências atuais](../../docs/evidencias/E12/README.md).


E13 executou este Program em processos Kestrel separados, com HTTP real e publicador/banco/broker reais. Não acrescentou endpoints/opções de falha. [Guia E13](../../docs/aprendizado/E13-RECUPERACAO-REENTREGA-E-CONCORRENCIA.md) · [Evidências](../../docs/evidencias/E13/README.md).
