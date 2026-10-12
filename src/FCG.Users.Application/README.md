# FCG.Users.Application

Casos de uso de usuários/autenticação. Depende somente de Domain; declara interfaces sem ASP.NET Core, EF ou criptografia concreta. **E09 concluída em 06/10/2026.**

- Usuarios: cadastro, consulta, atualização de dados, troca de perfil e inativação, com comandos/resultados próprios.
- Auth: login, refresh e logout conectados ao HTTP/banco real.
- Abstractions/Repositories: usuários, tokens e unidade de trabalho por usuário.
- Abstractions/Security: contratos de hash/JWT/refresh.
- NormalizadorIdentidade: regras usadas para entrada e identidade.

Atualização/perfil/inativação usam IUnidadeDeTrabalhoUsuarios para consultar o estado depois do bloqueio e confirmar usuário/auditoria juntos. Dados normalizados iguais, perfil atual ou conta já inativa não geram novo registro. Inativação e seu log usam o mesmo instante UTC.

Login verifica a senha inicialmente fora do bloqueio, depois relê atividade/e-mail/perfil dentro da unidade de trabalho. Refresh também consulta estado atual depois do bloqueio e retorna o par somente após rotação/commit confirmados. Logout coordena revogação pelo usuário autenticado informado pela borda HTTP.

Application coordena regras/transações por interface; Infrastructure implementa EF/PostgreSQL. A identidade/permissão HTTP é responsabilidade da Api. Substitutos ficam exclusivamente nos testes. Não há Outbox/mensageria antes das E10–E13.

[Guia E09](../../docs/aprendizado/E09-OPERACOES-PROTEGIDAS-E-AUDITORIA.md), [guia E08](../../docs/aprendizado/E08-REFRESH-LOGOUT-E-CONCORRENCIA.md) e [mapa E04](../../docs/planejamento/MAPA-EXTRACAO-IDENTITY-E04.md).
