# FCG.Users.Infrastructure

PostgreSQL próprio e segurança concreta do UsersAPI. **E09 concluída em 06/10/2026**, reutilizando a migration inicial da E05.

| Grupo | Responsabilidade |
|---|---|
| Data/UsersDbContext | Usuario, Perfil, Token e LogUsuario, com contexto scoped. |
| Data/Mappings e Migrations | Schema, limites, índices/checks/FKs, perfis estáveis e migration/snapshot próprios. |
| Data/UsersDbContextFactory | Ferramenta EF via ConnectionStrings__UsersDatabase. |
| RepositorioUsuarios | Cadastro/atualização com auditoria obrigatória e uma gravação atômica. |
| UnidadeDeTrabalhoUsuarios | Transação e SELECT FOR UPDATE parametrizado por usuário; participação na transação externa. |
| RepositorioTokens | Hash do refresh, rotação condicional e logout sob a mesma coordenação. |
| InicializadorAdministradorLocal | Primeiro administrador explícito, transação e bloqueio na linha do perfil. |
| Security | PBKDF2/Identity compatíveis, RSA externo/kid, emissão JWT e refresh aleatório. |
| DependencyInjection | Registros de persistência/hash/tokens/unidade de trabalho e validação de configuração. |

ObterPorIdAsync rastreia para atualização no mesmo contexto; consultas de autenticação/e-mail usam AsNoTracking. AtualizarAsync exige usuário rastreado e LogUsuario do mesmo alvo. O EF grava somente campos alterados, incluindo auditoria na mesma transação. Índices conhecidos traduzem conflitos e não deixam logs órfãos. Outras falhas provocam rollback/tratamento HTTP genérico.

A unidade de trabalho bloqueia a linha antes da consulta atual e coordena edição/perfil/inativação/login/refresh/logout. Chamadas internas participam da transação do mesmo contexto sem commit independente. Só a transação externa confirma o resultado; exceção limpa rastreamento e dispose desfaz a transação.

A rotação revoga somente um refresh ainda válido e inclui o sucessor atomicamente. Logout alcança sessões confirmadas antes de obter o bloqueio. Inativação não remove sessões históricas: login/refresh conferem Ativo; access anterior conserva validade até expirar.

Hash de cadastro: PBKDF2-SHA256, salt aleatório, 100.000 iterações; verificação preserva compatibilidade aprovada com PBKDF2/Identity. Refresh: 64 bytes aleatórios em Base64URL, SHA-256 no banco, duração configurada de sete dias. JWT: RS256, sub/role/iat/jti, issuer/audience/exp/nbf, access configurado para 15 minutos. PEM privado externo pertence ao emissor; públicas confiáveis são selecionadas exatamente por kid.

Sem migrations automáticas no startup. E06–E09 não alteram schema. Outbox/RabbitMQ pertencem às E10–E13.

[Guia E09](../../docs/aprendizado/E09-OPERACOES-PROTEGIDAS-E-AUDITORIA.md), [persistência E05](../../docs/aprendizado/E05-PERSISTENCIA-PROPRIA.md), [segurança E07](../../docs/aprendizado/E07-LOGIN-E-JWT-RS256.md) e [sessões E08](../../docs/aprendizado/E08-REFRESH-LOGOUT-E-CONCORRENCIA.md).
