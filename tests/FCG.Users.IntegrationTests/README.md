# Testes de integração do UsersAPI

**281 aprovados em 06/10/2026**, zero falhas/ignorados. Resultado atual da E09, com hash/JWT/configuração, HTTP e PostgreSQL reais.

| Classe | Casos |
|---|---:|
| TestesAdministracaoHttp | 39 |
| TestesAuditoriaCadastro | 2 |
| TestesAutorizacaoJwtHttp | 39 |
| TestesCadastroHttp | 22 |
| TestesConcorrenciaAdministracao | 8 |
| TestesConfiguracaoJwt | 38 |
| TestesConfiguracaoPersistencia | 6 |
| TestesEstruturaBanco | 12 |
| TestesHashSenha | 17 |
| TestesHostHttp | 19 |
| TestesInicializacaoAdministrador | 9 |
| TestesLoginHttp | 19 |
| TestesRepositorioTokens | 8 |
| TestesRepositorioUsuarios | 7 |
| TestesServicosTokens | 12 |
| TestesSessaoHttp | 24 |

Testcontainers cria PostgreSQL 16-alpine descartável com migration própria, porta aleatória e dados sintéticos. Não usa monólito, volume de desenvolvimento ou PEMs do usuário. Factories têm chaves temporárias de teste. Controllers __testes/e07 são carregados apenas nas factories que os solicitam; ausência no host normal continua verificada.

TestesAdministracaoHttp verifica titular/admin, 401/403/404/400/409, DTO seguro, campos extras ignorados, cadastro administrativo, perfil novo no refresh, inativação idempotente e rollback de três auditorias. Falhas são induzidas apenas nos bancos descartáveis e restrições temporárias removidas em finally.

TestesConcorrenciaAdministracao cobre dados atuais na emissão: perfil/inativação × login/refresh × duas ordens. Interceptadores pausam comandos com o lock retido; pg_stat_activity comprova espera no bloqueio FOR UPDATE. Depois confirma status, role, banco e auditoria.

TestesInicializacaoAdministrador verifica guardas de ambiente/banco, duas inicializações concorrentes, senha preservada e rollback de cadastro/log. O subprocesso também executa o Program real duas vezes sem JWT/HTTP. Há nove casos nesta classe.

Regressões E07/E08 preservadas: hashes PBKDF2/Identity, JWT somente RS256, kid/issuer/audience/prazo/identidade, públicas coexistentes/reinício/skew, refresh por hash, rotação/replay/logout, falha de persistência e duas ordens de logout/refresh. Access anterior conserva validade conforme contrato.

Na raiz, após Release e com Docker funcionando:

```powershell
dotnet test tests/FCG.Users.IntegrationTests -c Release --no-build --no-restore --logger 'trx;LogFileName=e09-integracao-final-2026-10-06.trx' --results-directory TestResults/E09
```

E09: `226 - 1 teste antigo de GET ausente + 39 administração + 8 concorrência + 9 inicialização = 281`. O [registro E09](../../docs/evidencias/E09/README.md) reúne 106 unitários, estes 281 casos e a demonstração Kestrel real. Ainda não depende de RabbitMQ.
