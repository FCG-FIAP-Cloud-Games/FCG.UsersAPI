using System.Text.Json;
using FCG.Notifications.Application.Messaging.Contracts;
using MassTransit;
using EventoIncompativel = FCG.Users.MessagingProof.Incompativel.UserCreatedEvent;

namespace FCG.Users.MessagingProof;

internal static class Laboratorio
{
    private const string Exchange = "FCG.Notifications.Application.Messaging.Contracts:UserCreatedEvent";
    private const string Urn = "urn:message:FCG.Notifications.Application.Messaging.Contracts:UserCreatedEvent";
    private const string ExchangeIncompativel = "FCG.Users.MessagingProof.Incompativel:UserCreatedEvent";
    private const string Fila = "notifications-user-created";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task ExecutarAsync(string notificationsDll, string output, string commit)
    {
        var cases = new List<object>();
        var vhost = "e10-" + Guid.NewGuid().ToString("N");
        using var broker = new BrokerApi(vhost);
        IBusControl? bus = null;
        ProcessoNotifications? notifications = null;
        object? topology = null;
        string? version = null;
        string? failure = null;
        string? failureDetail = null;
        try
        {
            if (!File.Exists(notificationsDll)) throw new InvalidOperationException("DLL de referência não encontrada.");
            await broker.InicializarAsync(vhost);
            var overview = await broker.VisaoGeralAsync() ?? throw new InvalidOperationException("Broker não respondeu.");
            version = overview.GetProperty("rabbitmq_version").GetString();
            bus = Bus.Factory.CreateUsingRabbitMq(config =>
            {
                config.Host("127.0.0.1", vhost, host =>
                {
                    host.Username(BrokerApi.Ambiente("E10_USERS_USER"));
                    host.Password(BrokerApi.Ambiente("E10_USERS_PASSWORD"));
                    host.PublisherConfirmation = true;
                });
                config.Message<UserCreatedEvent>(message => message.SetEntityName(Exchange));
            });
            using (var start = new CancellationTokenSource(TimeSpan.FromSeconds(30))) await bus.StartAsync(start.Token);

            // 1. O confirm sozinho permite sucesso mesmo sem destinatário.
            await PublicarAsync(bus, NovoEvento(), mandatory: false);
            Exigir(await broker.FilaAsync(Fila) is null, "A fila já existia antes do provisionamento.");
            Aprovar("01-confirm-sem-rota", new { publishConcluiu = true, filaExiste = false, resultado = "Sucesso de transporte sem destinatário; insuficiente para a Outbox." });

            // 2. A mesma biblioteca pode sinalizar o retorno NO_ROUTE.
            var recovered = NovoEvento();
            await ExigirRetornoAsync(() => PublicarAsync(bus, recovered, mandatory: true));
            Aprovar("02-mandatory-sem-rota", new { recovered.EventId, recovered.CorrelationId, retorno = "MessageReturnedException / 312-NO_ROUTE", elegivelParaPublishedAt = false });

            // 3. Provisionamento pertence ao laboratório, antes de iniciar o consumidor.
            await broker.ProvisionarAsync(Exchange, Fila);
            await PublicarAsync(bus, recovered, mandatory: true);
            await AguardarAsync(async () => Quantidade(await broker.FilaAsync(Fila), "messages") == 1, "Mensagem não chegou à fila durável.");
            var queue = await broker.FilaAsync(Fila) ?? throw new InvalidOperationException("Fila ausente.");
            var exchange = await broker.ExchangeAsync(Exchange) ?? throw new InvalidOperationException("Exchange ausente.");
            Exigir(queue.GetProperty("durable").GetBoolean() && !queue.GetProperty("auto_delete").GetBoolean(), "Fila não durável.");
            Exigir(Quantidade(queue, "consumers") == 0, "Um consumidor já estava ativo.");
            Exigir(exchange.GetProperty("durable").GetBoolean() && exchange.GetProperty("type").GetString() == "fanout", "Exchange inesperado.");
            var wire = await broker.EspiarAsync(Fila);
            ValidarEnvelope(wire, recovered);
            topology = new { vhost, exchange = Exchange, tipo = "fanout", durable = true, endpointExchange = Fila, queue = Fila, routingKey = "", errorQueue = Fila + "_error", skippedQueue = Fila + "_skipped" };
            Aprovar("03-consumidor-parado-e-envelope", new { recovered.EventId, recovered.CorrelationId, consumidores = 0, mensagens = 1, deliveryMode = 2, contentType = "application/vnd.masstransit+json", messageType = Urn, camposDeNegocio = 7, recuperacaoPreservouEvento = true });

            // 4. Executa a DLL original do colega em outro processo; não um consumer de teste.
            notifications = new ProcessoNotifications(notificationsDll, vhost);
            await AguardarAsync(async () => !notifications.Encerrou && Quantidade(await broker.FilaAsync(Fila), "consumers") > 0 && notifications.Concluidos(recovered.EventId) == 1 && Quantidade(await broker.FilaAsync(Fila), "messages") == 0, "Notifications não concluiu o evento.");
            Aprovar("04-notifications-real", new { recovered.EventId, processamentosConcluidos = notifications.Concluidos(recovered.EventId), mensagensPendentes = 0, entrega = "Simulação de boas-vindas no ConsoleNotificationSender; sem SMTP." });

            // 5. JSON com os mesmos campos não substitui a identidade correta.
            var incompatible = NovoEvento();
            var wrongType = new EventoIncompativel(incompatible.EventId, incompatible.CorrelationId, incompatible.OccurredAt, incompatible.Version, incompatible.UserId, incompatible.Name, incompatible.Email);
            await ExigirRetornoAsync(() => PublicarIncompativelAsync(bus, wrongType));
            await broker.VincularAsync(ExchangeIncompativel, Fila);
            await PublicarIncompativelAsync(bus, wrongType);
            await AguardarAsync(async () => Quantidade(await broker.FilaAsync(Fila + "_skipped"), "messages") == 1, "Contrato incompatível não foi para skipped.");
            var skipped = await broker.EspiarAsync(Fila + "_skipped");
            using (var document = JsonDocument.Parse(Convert.FromBase64String(skipped.GetProperty("payload").GetString()!)))
                Exigir(document.RootElement.GetProperty("messageType").EnumerateArray().Any(value => value.GetString() == "urn:message:" + ExchangeIncompativel), "URN do teste incompatível incorreta.");
            Exigir(notifications.Concluidos(incompatible.EventId) == 0, "Evento incompatível foi tratado como válido.");
            Aprovar("05-contrato-incompativel", new { incompatible.EventId, semBinding = "NO_ROUTE", comBindingForcado = "notifications-user-created_skipped", processamentosDeBoasVindas = 0 });

            // 6. Erro permanente reconhecido pelo consumer: sem as três retentativas.
            var invalid = NovoEvento() with { UserId = Guid.Empty };
            await PublicarAsync(bus, invalid, mandatory: true);
            await AguardarAsync(async () => Quantidade(await broker.FilaAsync(Fila + "_error"), "messages") == 1 && notifications.Invalidos(invalid.EventId) == 1, "Falha permanente não chegou à fila de erro.");
            Exigir(notifications.Invalidos(invalid.EventId) == 1 && notifications.Concluidos(invalid.EventId) == 0, "Tratamento permanente inesperado.");
            var error = await broker.EspiarAsync(Fila + "_error");
            var errorMessage = LerMensagem(error);
            Exigir(errorMessage.GetProperty("eventId").GetGuid() == invalid.EventId, "Identidade perdida na fila de erro.");
            Aprovar("06-falha-permanente", new { invalid.EventId, validacoesInvalidas = 1, processamentosConcluidos = 0, filaDeErro = Fila + "_error", identificadorPreservado = true });

            // 7. Mesmo EventId repetido gera dois efeitos na branch, que ainda não tem Inbox.
            var repeated = NovoEvento();
            await PublicarAsync(bus, repeated, mandatory: true);
            await AguardarAsync(() => Task.FromResult(notifications.Concluidos(repeated.EventId) == 1), "Primeira ocorrência não processada.");
            await PublicarAsync(bus, repeated, mandatory: true);
            await AguardarAsync(() => Task.FromResult(notifications.Concluidos(repeated.EventId) == 2), "Repetição não observada.");
            Aprovar("07-reentrega-sem-inbox", new { repeated.EventId, publicacoes = 2, processamentosConcluidos = 2, evidencia = "Necessidade de deduplicação persistente no card C28." });

            // 8. Quebra e correção de binding, sem trocar o evento lógico.
            var restored = NovoEvento();
            await broker.DesvincularAsync(Exchange, Fila);
            await ExigirRetornoAsync(() => PublicarAsync(bus, restored, mandatory: true));
            Exigir(notifications.Concluidos(restored.EventId) == 0, "Evento sem rota chegou ao consumer.");
            await broker.VincularAsync(Exchange, Fila);
            await PublicarAsync(bus, restored, mandatory: true);
            await AguardarAsync(() => Task.FromResult(notifications.Concluidos(restored.EventId) == 1), "Evento não recuperado após corrigir binding.");
            Aprovar("08-binding-corrigido-mesmo-evento", new { restored.EventId, restored.CorrelationId, falhaSemRota = "NO_ROUTE", eventIdPreservado = true, correlationIdPreservado = true, payloadPreservado = true, processamentosConcluidos = 1 });
        }
        catch (Exception exception)
        {
            failure = exception.GetType().Name;
            failureDetail = exception is InvalidOperationException or TimeoutException ? exception.Message : null;
            throw;
        }
        finally
        {
            notifications?.Dispose();
            if (bus is not null)
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await bus.StopAsync(stop.Token); }
                catch (Exception exception) { failure ??= "Encerramento: " + exception.GetType().Name; }
            }
            var report = new { etapa = "E10", executadoEmUtc = DateTimeOffset.UtcNow, resultado = failure is null ? "aprovado" : "interrompido", falha = failure, falhaDetalhe = failureDetail, cenariosAprovados = cases.Count, casos = cases, rabbitmq = new { version, image = Environment.GetEnvironmentVariable("E10_IMAGE_DIGEST"), topology }, masstransit = "8.3.0", target = "net8.0", notificationsCommit = commit, producer = "Ferramenta isolada; cadastro e Outbox ainda não conectados.", dados = "Somente sintéticos; sem payload pessoal ou segredos no relatório." };
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(report, JsonOptions));
            if (failure is not null) Console.Error.WriteLine($"Cenários aprovados antes da interrupção: {cases.Count}; falha: {failure}.");
        }

        if (failure is not null) throw new InvalidOperationException("Falha ao encerrar a prova.");

        void Aprovar(string name, object detail)
        {
            cases.Add(new { cenario = name, resultado = "aprovado", detalhes = detail });
            Console.WriteLine($"Aprovado: {name}");
        }
    }

    private static UserCreatedEvent NovoEvento() => new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 1, Guid.NewGuid(), "Pessoa Sintética E10", "e10@example.test");

    private static async Task PublicarAsync(IBus bus, UserCreatedEvent message, bool mandatory)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await bus.Publish(message, context =>
        {
            context.MessageId = message.EventId;
            context.CorrelationId = message.CorrelationId;
            context.Durable = true;
            context.Mandatory = mandatory;
        }, timeout.Token);
    }

    private static async Task PublicarIncompativelAsync(IBus bus, EventoIncompativel message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await bus.Publish(message, context =>
        {
            context.MessageId = message.EventId;
            context.CorrelationId = message.CorrelationId;
            context.Durable = true;
            context.Mandatory = true;
        }, timeout.Token);
    }

    private static async Task ExigirRetornoAsync(Func<Task> publish)
    {
        try { await publish(); }
        catch (MessageReturnedException exception) when (exception.Message.Contains("312-NO_ROUTE", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("A publicação sem rota não retornou NO_ROUTE.");
    }

    private static void ValidarEnvelope(JsonElement wire, UserCreatedEvent expected)
    {
        var properties = wire.GetProperty("properties");
        Exigir(properties.GetProperty("content_type").GetString() == "application/vnd.masstransit+json", "Content-type incorreto.");
        Exigir(properties.GetProperty("delivery_mode").GetInt32() == 2, "Mensagem não persistente.");
        Exigir(Guid.Parse(properties.GetProperty("message_id").GetString()!) == expected.EventId, "MessageId diferente do EventId.");
        Exigir(Guid.Parse(properties.GetProperty("correlation_id").GetString()!) == expected.CorrelationId, "CorrelationId de transporte incorreto.");
        using var document = JsonDocument.Parse(Convert.FromBase64String(wire.GetProperty("payload").GetString()!));
        var envelope = document.RootElement;
        Exigir(envelope.GetProperty("messageType").EnumerateArray().Any(type => type.GetString() == Urn), "Identidade de contrato ausente.");
        Exigir(envelope.GetProperty("messageId").GetGuid() == expected.EventId, "MessageId do envelope incorreto.");
        Exigir(envelope.GetProperty("correlationId").GetGuid() == expected.CorrelationId, "Correlação do envelope incorreta.");
        var message = envelope.GetProperty("message");
        string[] allowed = ["eventId", "correlationId", "occurredAt", "version", "userId", "name", "email"];
        Exigir(message.EnumerateObject().Select(value => value.Name).Order().SequenceEqual(allowed.Order()), "Campos extras ou ausentes no payload.");
        Exigir(message.GetProperty("eventId").GetGuid() == expected.EventId && message.GetProperty("correlationId").GetGuid() == expected.CorrelationId, "IDs de negócio alterados.");
        Exigir(message.GetProperty("occurredAt").GetDateTimeOffset() == expected.OccurredAt && message.GetProperty("occurredAt").GetDateTimeOffset().Offset == TimeSpan.Zero, "Instante UTC incorreto.");
        Exigir(message.GetProperty("version").GetInt32() == 1 && message.GetProperty("userId").GetGuid() == expected.UserId, "Versão ou usuário incorreto.");
        Exigir(message.GetProperty("name").GetString() == expected.Name && message.GetProperty("email").GetString() == expected.Email, "Dados de demonstração alterados.");
    }

    private static JsonElement LerMensagem(JsonElement wire)
    {
        using var document = JsonDocument.Parse(Convert.FromBase64String(wire.GetProperty("payload").GetString()!));
        return document.RootElement.GetProperty("message").Clone();
    }

    private static int Quantidade(JsonElement? queue, string field) => queue is { } value && value.TryGetProperty(field, out var count) ? count.GetInt32() : -1;

    private static void Exigir(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task AguardarAsync(Func<Task<bool>> condition, string failure)
    {
        var deadline = DateTime.UtcNow.AddSeconds(50);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(500);
        }
        throw new TimeoutException(failure);
    }
}
