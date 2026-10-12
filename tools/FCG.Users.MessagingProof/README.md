# Prova de mensageria da E10

Ferramenta .NET 8 separada dos seis projetos de `FCG.Users.sln`. Usa MassTransit.RabbitMQ 8.3.0, com versão centralizada. Nenhum projeto de produção referencia esta ferramenta ou Notifications.

Execute pelo [roteiro PowerShell](../../scripts/Verificar-MensageriaE10.ps1), que cria um broker descartável, configura credenciais separadas e passa a DLL original do consumidor. A revisão de Notifications é conferida antes de começar.

```powershell
.\scripts\Verificar-MensageriaE10.ps1 -NotificationsRepoPath 'C:\GIT\FCG.NotificationsAPI'
```

Use um clone de referência no commit `78b512477298cb9b37dbfe38504030d9c3d09a51`. O caminho acima é um exemplo; adapte ao seu clone. Não troque o checkout de um colega com trabalho pendente para executar a prova.

Pré-requisitos: SDK compatível com `global.json`, PowerShell 7.4+, Docker Linux, portas 5672/15672/5089 livres e acesso aos pacotes/imagem. O runner compila projetos `.csproj`, sem depender do formato `.slnx` do colega.

Oito cenários reais: confirm sem rota, mandatory sem rota, fila durável com consumidor parado/envelope, consumo pela Notifications original, namespace incompatível, mensagem inválida em `_error`, reentrega sem Inbox e correção de binding com o mesmo evento.

O resultado padrão é `TestResults/E10/rabbitmq-real.json`, ignorado pelo Git. Exit code 0 exige as oito aprovações e encerramento do bus sem erro; qualquer falha interrompe a prova. As credenciais temporárias são apagadas/restauradas e somente o container identificado pelo runner é removido, incluindo seus volumes anônimos. Os logs brutos do consumidor ficam em memória; o relatório não contém nome/e-mail ou segredos.

A identidade atual do consumidor é mantida na cópia local do record. O record de namespace diferente existe apenas para demonstrar incompatibilidade. Os sete campos pertencem ao contrato do #48; não são uma entidade EF.

Veja o [capítulo didático](../../docs/aprendizado/E10-CONTRATO-E-PROVA-RABBITMQ.md), o [contrato](../../docs/contratos/CONTRATO-USERCREATED-E-TOPOLOGIA.md) e as [evidências](../../docs/evidencias/E10/README.md).
