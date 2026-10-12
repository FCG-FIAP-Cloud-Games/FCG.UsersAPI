# Roteiros locais — E11 a E15

Executar os verificadores na raiz do UsersAPI. E15 exige PowerShell 7.4+, Git e Docker Linux; o build .NET ocorre dentro das imagens. Os verificadores E11–E14 também exigem SDK .NET 8. Para apenas usar a aplicação, siga o [guia curto Docker](../docs/operacao/EXECUTAR-DEMONSTRACAO-DOCKER.md); nenhum script PowerShell é obrigatório.

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

## Reproduzir a E15

```powershell
.\scripts\Verificar-DemonstracaoE15.ps1
```

A prova usa outra cópia em TestResults/E15 e outro nome de projeto Compose, com portas livres. Não usa as chaves locais antigas, o banco do monólito ou os volumes da demonstração normal fcg-users-demo. Faz a subida pelo mesmo Compose, verifica os fluxos essenciais, para sem apagar volumes e confirma preservação na segunda subida. Também confere as recusas explícitas da preparação fora do ambiente permitido.

Ao terminar, remove somente containers/rede/volumes do projeto aleatório criado pela própria prova. Conserva a cópia de fonte e o relatório sanitizado em TestResults/E15/demonstracao-e15.json, ignorados pelo Git. Não imprime tokens, senhas reais, conexão pessoal ou chave privada. [Evidências E15](../docs/evidencias/E15/README.md).

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
