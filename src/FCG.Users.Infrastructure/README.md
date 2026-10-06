# FCG.Users.Infrastructure

Persistência própria do UsersAPI em PostgreSQL, implementada na E05, e segurança concreta de cadastro/autenticação conectada nas E06–E08.

- `Data/UsersDbContext`: unidade de trabalho com Usuario, Perfil, Token e LogUsuario.
- `Data/Mappings`: tipos, limites, índices, checks, FKs e perfis iniciais.
- `Data/Migrations`: migration inicial própria e snapshot. A E06 não altera o schema.
- `Data/UsersDbContextFactory`: ferramenta EF via `ConnectionStrings__UsersDatabase`.
- `Repositories`: armazenamento real de usuários e sessões, com rotação transacional.
- `Security/HashSenhaPbkdf2`: geração de hash do cadastro, preservando o formato PBKDF2-SHA256 da origem, com salt aleatório e 100.000 iterações.
- `Security/ServicoHashSenha`: verificação do formato PBKDF2 da origem e dos formatos Identity compatíveis, usada pelo login conectado na E07.
- `Security/ConfiguracaoJwt` e `ChavesJwtRsa`: configuração e carregamento de RSA externo, par ativo correspondente e lista pública confiável por kid.
- `Security/ServicoTokenJwt`: access token RS256 com claims mínimos e prazo de 15 minutos na configuração aprovada.
- `Security/ServicoRefreshToken`: 64 bytes aleatórios em Base64URL, SHA-256 persistido e 7 dias na configuração aprovada.
- `DependencyInjection`: validação da configuração e registros de persistência/segurança; chaves com ciclo de vida singleton e descarte pelo contêiner.

`ObterPorIdAsync` rastreia a entidade para permitir atualizações por campo alterado. `AtualizarAsync` exige a entidade carregada no mesmo contexto. As consultas de autenticação/leitura por e-mail usam `AsNoTracking`.

Na E06, `TentarAdicionarAsync` recebe o usuário e o registro de auditoria criado pelo caso de uso. Confere se os identificadores correspondem, adiciona ambos e executa um único `SaveChangesAsync`. A transação dessa gravação evita que uma falha na inclusão do log deixe um usuário sem auditoria. Violações dos índices conhecidos de e-mail/CPF são traduzidas em resultados de conflito; outras falhas seguem para o tratamento de erro da API. Os testes reais comprovam rollback e disputa concorrente.

Não há migration automática no início da API. A auditoria de alteração de dados, troca de perfil e inativação será conectada na E09. JWT e primeiro refresh já estão conectados no login. Refresh/logout HTTP estão conectados na E08; Outbox/RabbitMQ nas E10–E13. E06–E08 não alteram o schema.

Consulte o [guia E05](../../docs/aprendizado/E05-PERSISTENCIA-PROPRIA.md) para persistência e o [guia E06](../../docs/aprendizado/E06-CADASTRO-HTTP-E-AUDITORIA.md) para o fluxo de cadastro. O [guia E07](../../docs/aprendizado/E07-LOGIN-E-JWT-RS256.md) explica autenticação, validação e chaves.

Na E08, login, refresh e logout obtêm um bloqueio parametrizado FOR UPDATE na linha do usuário antes de alterar sessões. Logout revoga depois do bloqueio, alcançando o sucessor de refresh concorrente confirmado antes. A rotação exige uma linha ainda válida, revoga/inclui numa transação e confirma sucesso só após commit. Novos logins posteriores continuam permitidos.

O [guia E08](../../docs/aprendizado/E08-REFRESH-LOGOUT-E-CONCORRENCIA.md) explica concorrência/limites e as [evidências](../../docs/evidencias/E08/README.md) registram PostgreSQL real e Kestrel.
