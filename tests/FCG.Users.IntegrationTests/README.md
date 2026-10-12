# Testes de integração do UsersAPI

**367 aprovados na E14 em 08/10/2026**, em cópia limpa e incluindo a API publicada, zero falhas/ignorados. Hash/JWT/configuração, HTTP, PostgreSQL e transporte RabbitMQ nos cenários correspondentes. E13 inclui processos reais em Kestrel.

| Classe | Casos |
|---|---:|
| TestesAdministracaoHttp | 39 |
| TestesAuditoriaCadastro | 2 |
| TestesAutorizacaoJwtHttp | 39 |
| TestesCadastroHttp | 22 |
| TestesConcorrenciaAdministracao | 8 |
| TestesConfiguracaoJwt | 38 |
| TestesConfiguracaoOutbox | 26 |
| TestesConfiguracaoPersistencia | 6 |
| TestesEstruturaBanco | 12 |
| TestesHashSenha | 17 |
| TestesHostHttp | 19 |
| TestesInicializacaoAdministrador | 9 |
| TestesLoginHttp | 19 |
| TestesOutboxCadastro | 18 |
| TestesOutboxCadastroHttp | 16 |
| TestesPassagemE14 | 1 |
| TestesProcessadorOutbox | 14 |
| TestesPublicadorOutboxHost | 4 |
| TestesRecuperacaoOutboxE13 | 5 |
| TestesRepositorioTokens | 8 |
| TestesRepositorioUsuarios | 7 |
| TestesServicosTokens | 12 |
| TestesSessaoHttp | 24 |
| TestesTransporteRabbitMq | 2 |

Testcontainers cria PostgreSQL 16-alpine descartável com migration própria, porta aleatória e dados sintéticos. Não usa monólito, volume de desenvolvimento ou PEMs do usuário. Factories têm chaves temporárias de teste. Controllers __testes/e07 são carregados apenas nas factories que os solicitam; ausência no host normal continua verificada.

TestesAdministracaoHttp verifica titular/admin, 401/403/404/400/409, DTO seguro, campos extras ignorados, cadastro administrativo, perfil novo no refresh, inativação idempotente e rollback de três auditorias. Falhas são induzidas apenas nos bancos descartáveis e restrições temporárias removidas em finally.

TestesConcorrenciaAdministracao cobre dados atuais na emissão: perfil/inativação × login/refresh × duas ordens. Interceptadores pausam comandos com o lock retido; pg_stat_activity comprova espera no bloqueio FOR UPDATE. Depois confirma status, role, banco e auditoria.

TestesInicializacaoAdministrador verifica guardas de ambiente/banco, duas inicializações concorrentes, senha preservada e rollback de cadastro/log/Outbox. O subprocesso também executa o Program real duas vezes sem JWT/HTTP. Há nove casos nesta classe.

Regressões E07/E08 preservadas: hashes PBKDF2/Identity, JWT somente RS256, kid/issuer/audience/prazo/identidade, públicas coexistentes/reinício/skew, refresh por hash, rotação/replay/logout, falha de persistência e duas ordens de logout/refresh. Access anterior conserva validade conforme contrato.

Na raiz, após Release e com Docker funcionando:

```powershell
dotnet test tests/FCG.Users.IntegrationTests -c Release --no-build --no-restore --logger 'trx;LogFileName=e14-integracao.trx' --results-directory TestResults/E14
```

E11: 281 anteriores + 18 persistência Outbox + 16 HTTP Outbox = 315. E12 acrescentou 46, totalizando 361. E13 acrescentou cinco, totalizando 366. E14 acrescentou uma aceitação com API publicada, totalizando 367. Os casos existentes foram reexecutados e atualizados para exigir o evento nos cadastros.

A nova cobertura inclui falha na Outbox/commit, rollback da transação externa, correlação, snapshot preservado, EventId repetido, guards do banco e reuso do contexto após cancelamento antes do SQL. A presença explicitamente vazia de X-Correlation-ID é injetada no pipeline TestServer porque HttpClient omite esse valor.

[Guia E11](../../docs/aprendizado/E11-CADASTRO-E-OUTBOX-ATOMICA.md) e [evidências históricas E11](../../docs/evidencias/E11/README.md) conservam o resultado daquela entrega. As [evidências históricas E13](../../docs/evidencias/E13/README.md) reúnem 109 unitários e os 366 casos daquela etapa; as [evidências atuais E14](../../docs/evidencias/E14/README.md) reúnem 109 unitários e estes 367 casos.

## Publicador E12

Os 46 casos específicos cobrem configuração, transporte, processamento e host. Verificar-PublicadorE12.ps1 compila a solução e os executa com recursos descartáveis.

Transporte/host usam RabbitMQ real nos cenários correspondentes; o processador usa PostgreSQL real com transporte substituto para controlar falhas. HTTP usa Program/worker reais em TestServer, não Kestrel. Nenhum consumer Notifications/email é executado.

Factories removem provedores de logs do sistema e conservam coletores explícitos de cada cenário. A coleção RabbitMQ não concorre com outras coleções, evitando interferência entre hosts descartáveis; provas de concorrência explícita do processador continuam executadas.

A E12 verifica fila durável sem consumidor, ausência/correção de binding, IDs/payload, timeout, rollback/cancelamento e falha de commit após envio controlado. Reinício, crash e múltiplos processos com broker real continuam na E13.

[Guia E12](../../docs/aprendizado/E12-PUBLICADOR-OUTBOX-E-RABBITMQ.md) · [Runner](../../scripts/Verificar-PublicadorE12.ps1) · [Evidências E12](../../docs/evidencias/E12/README.md).

## Recuperação E13

Cinco provas com processos reais dotnet/Program em Kestrel, HTTP por socket, PostgreSQL e RabbitMQ reais. Usam coleção/broker próprios para poder parar/retomar o container sem afetar outro cenário.

TestesRecuperacaoOutboxE13 cobre retorno do broker na mesma API, reinício com pendência, crash após publicação antes da baixa, duas instâncias e cadastro revertido. Trigger/advisory lock existem apenas no banco descartável para pausar a atualização e observar a janela de queda/concorrência. Nenhum gancho foi acrescentado na API.

O contador pode continuar zero depois de um envio cuja transação não confirmou. A prova de crash observa duas cópias do mesmo EventId com payload igual e apenas uma tentativa final confirmada no banco. Não demonstra idempotência ou envio de e-mail em Notifications.

Verificar-RecuperacaoE13.ps1 executa esses cinco casos e extrai seus estados/IDs/hash do TRX. Eles também passaram dentro da suíte completa, incluindo os casos E11/E12.

[Guia E13](../../docs/aprendizado/E13-RECUPERACAO-REENTREGA-E-CONCORRENCIA.md) · [Runner](../../scripts/Verificar-RecuperacaoE13.ps1) · [Evidências E13](../../docs/evidencias/E13/README.md).

## Passagem E14

Um novo caso de aceitação em Acceptance/TestesPassagemE14 executa a DLL publicada com seus próprios .deps.json/.runtimeconfig.json, banco/broker/chaves novos e os três caminhos de criação. Valida sessão/permissões, pública RSA, quatro ações auditadas, snapshot e três envelopes reais. Não executa Notifications ou envia e-mail.

O runner Verificar-PassagemE14.ps1 faz restore/build/publish em cópia de fonte sem bin/obj/segredos e executa unitários + toda integração. O auxiliar ArtefatoUsersApiE14 também pode publicar localmente para a execução isolada do novo caso. O auxiliar de processos E13 recebeu uma opção explícita para o artefato, conservando a inicialização das provas anteriores.

[Guia E14](../../docs/aprendizado/E14-CONSOLIDACAO-E-PASSAGEM-PARA-O-GRUPO.md) · [Evidências E14](../../docs/evidencias/E14/README.md).
