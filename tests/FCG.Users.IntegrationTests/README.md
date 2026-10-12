# Testes de integração do UsersAPI

Na E08: **226 testes aprovados**, zero falhas/ignorados. Execução em 05/10/2026, com 24 novos cenários HTTP de sessão.

| Classe | Cenários executados |
|---|---:|
| TestesAuditoriaCadastro | 2 |
| TestesAutorizacaoJwtHttp | 39 |
| TestesCadastroHttp | 23 |
| TestesConfiguracaoJwt | 38 |
| TestesConfiguracaoPersistencia | 6 |
| TestesEstruturaBanco | 12 |
| TestesHashSenha | 17 |
| TestesHostHttp | 19 |
| TestesLoginHttp | 19 |
| TestesRepositorioTokens | 8 |
| TestesRepositorioUsuarios | 7 |
| TestesServicosTokens | 12 |
| TestesSessaoHttp | 24 |

O host básico usa conexão fictícia; cadastro/login/refresh/logout/persistência usam PostgreSQL 16 real descartável por Testcontainers, com migration própria, porta e senha de teste. Não usamos o banco do monólito, o volume de desenvolvimento ou as chaves privadas do usuário.

Todas as factories usam PEM temporário exclusivo dos testes. A configuração chega antes do entry point; chaves são descartadas após encerrar a factory. Helpers que excluem arquivos removem somente o que criaram, sem exclusão recursiva arbitrária. As rotas protegidas __testes/e07 são carregadas somente quando a factory as solicita, e sua ausência no host normal é testada.

A suíte demonstra login com PBKDF2/Identity e perfis Usuario/Administrador, preservação da resposta, claims mínimos, assinatura pública independente, refresh hash-only/7 dias, 401 genérico e erros sem segredos. Configuração inválida, chaves fracas/malformadas/incompatíveis, kid duplicado e PEM privado usado como público são recusados.

Middleware: assinatura/algoritmo/issuer/audience/prazo/kid/identidade inválidos retornam 401; Usuario em rota administrativa retorna 403. Reinício com o mesmo PEM preserva a validade; públicas antiga/nova coexistem até retirada; skew temporal é verificado.

Falhas reais de cadastro/auditoria e persistência do refresh são induzidas somente no banco descartável. Estruturas temporárias são removidas em finally. Logs são capturados incluindo exceções completas; senha, tokens, hashes e dados sintéticos não podem aparecer nos cenários que verificam isso. O rollback e os índices únicos não são substituídos por mocks.

Para executar com Docker funcionando:

```powershell
dotnet test tests/FCG.Users.IntegrationTests/FCG.Users.IntegrationTests.csproj -c Release --logger "trx;LogFileName=e08-integracao.trx" --results-directory TestResults/E08
```

TRX aprovado: TestResults/E08/e08-integracao-final-2026-10-05.trx. O [registro E08](../../docs/evidencias/E08/README.md) inclui os 100 unitários e o ciclo Kestrel real.

TestesSessaoHttp verifica rotação/replay, expiração exata, inativação, perfil/dados atuais, duas renovações, logout de todas as sessões por sub, isolamento de outro usuário e JWT antigo ainda válido. Falha real prova rollback. Interceptador só de testes pausa primeira revogação e pg_stat_activity comprova espera em FOR UPDATE: refresh primeiro e logout primeiro, sem deixar sessão renovável.

Dois cenários antigos exigiam ausência de refresh/logout e foram retirados: 204 − 2 + 24 = 226. Não depende de RabbitMQ nesta entrega.
