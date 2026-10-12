# Prova de mensageria da E10

Ferramenta .NET 8 separada dos seis projetos de FCG.Users.sln. Usa MassTransit.RabbitMQ 8.3.0, com versão centralizada. Nenhum projeto de produção referencia esta ferramenta ou a DLL Notifications.

Na E11, a ferramenta passou a referenciar FCG.Users.Application e usar a definição única de UserCreatedEvent. A cópia anterior em Contracts foi removida. A identidade e os sete campos foram preservados; o record de namespace diferente continua apenas na prova de incompatibilidade.

Compilação independente:

```powershell
dotnet build tools/FCG.Users.MessagingProof/FCG.Users.MessagingProof.csproj -c Release
```

A prova E10 foi executada anteriormente com Notifications no commit 78b512477298cb9b37dbfe38504030d9c3d09a51, em clone separado. O antigo scripts/Verificar-MensageriaE10.ps1 não está nesta branch; não foi restaurado automaticamente nesta entrega. Não apresentar o comando desse runner como disponível no checkout atual.

Oito cenários anteriores: confirm sem rota, mandatory sem rota, fila durável com consumidor parado/envelope, consumo pela Notifications original, namespace incompatível, mensagem inválida em _error, reentrega sem Inbox e correção de binding com o mesmo evento.

Na E11 foi revalidada a compilação com o contrato centralizado, sem repetir a execução RabbitMQ. A ferramenta não publica a Outbox da API; o publicador de produção será construído na E12 e terá sua própria prova com banco/broker reais.

[Contrato atual](../../docs/contratos/CONTRATO-USERCREATED-E11.md) e [evidências E11](../../docs/evidencias/E11/README.md).
