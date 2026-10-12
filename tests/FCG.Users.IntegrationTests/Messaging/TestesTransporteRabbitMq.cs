using System.Text.Json;
using FCG.Notifications.Application.Messaging.Contracts;
using FCG.Users.Application.Messaging;
using FCG.Users.Infrastructure.Messaging;
using FCG.Users.IntegrationTests.Support;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FCG.Users.IntegrationTests.Messaging;

[Collection(ColecaoMensageria.Nome)]
public sealed class TestesTransporteRabbitMq(RabbitMqFixture rabbit)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TransporteRealPreservaContratoIdsEDurabilidadeComConsumidorDesligado()
    {
        await using var cenario = await rabbit.NovoCenarioAsync();
        await using var servicos = CriarServicos(cenario.Configuracao);
        var bus = servicos.GetRequiredService<IBusControl>();
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(Cancelamento);
        prazo.CancelAfter(TimeSpan.FromSeconds(30));
        await bus.StartAsync(prazo.Token);
        try
        {
            var evento = Evento();
            await servicos.GetRequiredService<IPublicadorEventos>().PublicarAsync(evento, prazo.Token);
            var mensagens = await cenario.MensagemAsync();
            var mensagem = Assert.Single(mensagens.EnumerateArray());
            Assert.Equal(2, mensagem.GetProperty("properties").GetProperty("delivery_mode").GetInt32());
            Assert.Equal("application/vnd.masstransit+json", mensagem.GetProperty("properties").GetProperty("content_type").GetString());
            using var envelope = JsonDocument.Parse(Convert.FromBase64String(mensagem.GetProperty("payload").GetString()!));
            Assert.Equal(evento.EventId, envelope.RootElement.GetProperty("messageId").GetGuid());
            Assert.Equal(evento.CorrelationId, envelope.RootElement.GetProperty("correlationId").GetGuid());
            Assert.Contains(envelope.RootElement.GetProperty("messageType").EnumerateArray(),
                tipo => tipo.GetString() == DefinicaoUserCreatedEvent.Tipo);
            var payload = envelope.RootElement.GetProperty("message");
            Assert.Equal(7, payload.EnumerateObject().Count());
            Assert.Equal(evento.EventId, payload.GetProperty("eventId").GetGuid());
            Assert.Equal(evento.UserId, payload.GetProperty("userId").GetGuid());
            Assert.Equal(evento.Name, payload.GetProperty("name").GetString());
            Assert.Equal(evento.Email, payload.GetProperty("email").GetString());
            var fila = await cenario.FilaAsync();
            Assert.True(fila!.Value.GetProperty("durable").GetBoolean());
            Assert.Empty(fila.Value.GetProperty("consumer_details").EnumerateArray());
        }
        finally { await bus.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task TransporteRealSemBindingDevolveNoRouteSemDadosPessoaisNosLogs()
    {
        await using var cenario = await rabbit.NovoCenarioAsync();
        await cenario.DesvincularAsync();
        using var logs = new ColetorLogs();
        await using var servicos = CriarServicos(cenario.Configuracao, logs);
        var bus = servicos.GetRequiredService<IBusControl>();
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(Cancelamento);
        prazo.CancelAfter(TimeSpan.FromSeconds(30));
        await bus.StartAsync(prazo.Token);
        try
        {
            var evento = Evento();
            var erro = await Assert.ThrowsAsync<MessageReturnedException>(() =>
                servicos.GetRequiredService<IPublicadorEventos>().PublicarAsync(evento, prazo.Token));
            Assert.Contains("NO_ROUTE", erro.Message, StringComparison.Ordinal);
            Assert.Empty((await cenario.MensagemAsync()).EnumerateArray());
            Assert.DoesNotContain(evento.Name, logs.Texto, StringComparison.Ordinal);
            Assert.DoesNotContain(evento.Email, logs.Texto, StringComparison.Ordinal);
            Assert.DoesNotContain(cenario.Password, logs.Texto, StringComparison.Ordinal);
        }
        finally { await bus.StopAsync(CancellationToken.None); }
    }

    private static ServiceProvider CriarServicos(Dictionary<string, string?> dados, ColetorLogs? logs = null)
    {
        var servicos = new ServiceCollection();
        servicos.AddLogging(logging => { if (logs is not null) logging.AddProvider(logs); });
        servicos.AdicionarTransporteOutbox(new ConfigurationBuilder().AddInMemoryCollection(dados).Build());
        return servicos.BuildServiceProvider(validateScopes: true);
    }

    private static UserCreatedEvent Evento() => new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 1,
        Guid.NewGuid(), "Pessoa Sintética E12", "e12@exemplo.test");
}
