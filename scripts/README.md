# Roteiros locais — E11 a E17

Executar os verificadores na raiz do UsersAPI. O verificador E16 exige PowerShell 7.4+, Git e Docker Linux; o build .NET ocorre dentro das imagens. Os verificadores E11–E14 também exigem SDK .NET 8. Para apenas usar a aplicação, siga o [guia atual](../README.md); nenhum script PowerShell é obrigatório.

| Roteiro | Ação |
|---|---|
| [Verificar-DemonstracaoE15.ps1](Verificar-DemonstracaoE15.ps1) | Copia a fonte atual sem saídas/segredos, sobe Compose isolado, verifica HTTP/JWT/fila e repetição preservando dados/chaves. Descarta somente os volumes do projeto aleatório da prova. |
| [Verificar-PassagemE14.ps1](Verificar-PassagemE14.ps1) | Copia fonte sem saídas/segredos, compila/publica, gera SQL idempotente e executa toda a suíte com prova de artefato publicado. |
| [Gerar-ChavesJwtLocal.ps1](Gerar-ChavesJwtLocal.ps1) | Gera explicitamente RSA 2048 em .local/jwt; recusa substituição. Não altera o par antigo. |
| [Verificar-RecuperacaoE13.ps1](Verificar-RecuperacaoE13.ps1) | Compila Release e executa cinco provas reais de recuperação; gera estados/IDs/hash em TestResults/E13/recuperacao-e13.json. |
| [Verificar-PublicadorE12.ps1](Verificar-PublicadorE12.ps1) | Compila Release e executa os 46 casos específicos E12 com recursos descartáveis; gera TestResults/E12/publicador-e12.json. |
| [Configurar-AmbienteE11Local.ps1](Configurar-AmbienteE11Local.ps1) | Reutiliza .env.usersdb e o container esperado; carrega conexão/par RSA existente. -SemJwt prepara só banco. |
| [Aplicar-MigrationOutboxLocal.ps1](Aplicar-MigrationOutboxLocal.ps1) | Configura o banco local, restaura EF e aplica migrations atuais em Release. |
| [Verificar-OutboxLocal.ps1](Verificar-OutboxLocal.ps1) | Inicia Kestrel oculto com publicador desabilitado, cria conta sintética, consulta três registros e encerra seu processo. -Porta permite trocar 5091. |

## Reproduzir a E17 sem arquivos RabbitMQ

```powershell
pwsh -NoProfile -File scripts/Verificar-DemonstracaoE15.ps1 -SemArquivosRabbitMq
```

O parâmetro usa um projeto descartável novo e um broker sem configurações/definitions montados. Comprova que a fila e a exchange não existiam antes do Users, que a demonstração declara a rota e publica os eventos, e que dados, chaves e mensagens sobrevivem ao reinício. Não inicia Notifications nem altera o Compose do grupo.

Resultado sanitizado: `TestResults/E17/demonstracao-e17.json`. As evidências históricas foram preservadas fora do pacote de implementação. Esse roteiro é uma verificação opcional para quem desenvolve; o grupo incorpora somente o [fluxo atual de inicialização e exportação](../tools/FCG.Users.DemoSetup/README.md).


## Reproduzir a E16

O nome do arquivo permanece por compatibilidade:

```powershell
.\scripts\Verificar-DemonstracaoE15.ps1
```

O roteiro agora verifica a preparação interna em uma imagem e um container Users, sem serviço preparacao. Copia a fonte atual para TestResults/E16 e usa outro projeto Compose, portas livres e volumes novos.

Confere cadastro, login, autorização, assinatura RS256, auditoria, Outbox e fila; reinício preservando dados e chaves; UID 1654 e permissões dos PEMs; recusa de Production/ambientes contraditórios/flag inválida; flag false sem bootstrap; cancelamento real antes do HTTP.

A limpeza remove somente recursos identificados pelo UUID da própria prova. Preserva o ambiente normal e os recursos do grupo. O relatório sanitizado fica em TestResults/E16/demonstracao-e16.json, ignorado pelo Git.

Os resultados E15 de outubro são históricos do desenho anterior com preparador separado. Os roteiros E11–E14 continuam abaixo para suas respectivas verificações.

## Reproduzir a E14

```powershell
.\scripts\Verificar-PassagemE14.ps1
```

Exige Git na fonte original, SDK .NET 8, PowerShell 7.4+ e Docker Linux. Usa uma nova cópia em TestResults/E14/fonte-<UUID>; inclui fontes ainda não commitadas e exclui .git/bin/obj/.env/.local/PEMs. Usa o cache global NuGet normal. Não faz clone/push, não altera banco de desenvolvimento e não aplica o SQL gerado.

A prova E14 usa DLL/deps/runtimeconfig publicados, Kestrel e três criações; os demais testes continuam incluídos. Resumo sanitizado em passagem-e14.json, manifesto de fonte, SQL idempotente e dois TRX locais. Cópias antigas ficam guardadas; containers/PEMs/PIDs próprios dos testes são descartados.

Para estudo manual, [seguir o guia do zero](../docs/operacao/EXECUTAR-USERSAPI-DO-ZERO.md). Gerar-ChavesJwtLocal.ps1 deve ser executado explicitamente; .local/ é ignorada. Não modifica as chaves externas antigas utilizadas pelos roteiros E11.

[Guia E14](../docs/aprendizado/E14-CONSOLIDACAO-E-PASSAGEM-PARA-O-GRUPO.md) · [Matriz dos cards](../docs/planejamento/MATRIZ-PASSAGEM-CARDS-52-55-E14.md).

## Reproduzir a E13

```powershell
.\scripts\Verificar-RecuperacaoE13.ps1
```

Inicia a API/worker em processos ocultos Kestrel, PostgreSQL e RabbitMQ descartáveis. Prova retorno do broker, reinício com pendência, queda depois do envio, duas APIs e cadastro revertido. O runner extrai cinco relatórios do TRX, com IDs sintéticos, estados e hash sem payload pessoal.

Não usa o volume de desenvolvimento ou PEMs pessoais. Encerra somente os próprios PIDs e controla apenas containers/bancos dos testes. As barreiras de banco não existem no código de produção. Não inicia Notifications ou envia e-mail.

[Guia E13](../docs/aprendizado/E13-RECUPERACAO-REENTREGA-E-CONCORRENCIA.md) · [Evidências](../docs/evidencias/E13/README.md) · [Roteiro operacional](../docs/operacao/RECUPERAR-PENDENCIAS-OUTBOX.md).

## Reproduzir a E12

```powershell
.\scripts\Verificar-PublicadorE12.ps1
```

Testcontainers cria RabbitMQ/PostgreSQL temporários com portas e credenciais próprias. Os testes não usam o volume de desenvolvimento, o banco do monólito ou os PEMs pessoais. A topologia de fila/bindings é provisionada pela fixture, somente para os cenários. Não há consumidor Notifications ou envio efetivo de e-mail.

O relatório contém apenas resumo/cenários, sem credenciais. Os TRX e logs locais ficam em TestResults, ignorado pelo Git. [Guia E12](../docs/aprendizado/E12-PUBLICADOR-OUTBOX-E-RABBITMQ.md) · [Evidências](../docs/evidencias/E12/README.md).

## Reproduzir a E11 no banco de desenvolvimento

Os três roteiros E11 reutilizam o ambiente já existente. Não substituem credenciais/chaves/containers. Verificam o binding PostgreSQL 127.0.0.1:55432 antes de iniciar o container esperado.

A demonstração E11 deixa a conta sintética e sua Outbox no banco. Desabilita explicitamente o worker do processo iniciado, preservando Attempts zero/PublishedAt nulo. Reexecutar cria outra conta; não apaga a anterior. Essa prova não é a publicação E12.

[Reprodução E11](../docs/aprendizado/E11-CADASTRO-E-OUTBOX-ATOMICA.md). Os roteiros anteriores não estavam nesta branch ao iniciar E11; apenas os arquivos atuais foram criados.
