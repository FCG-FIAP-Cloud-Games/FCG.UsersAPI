using FCG.Notifications.Application.Messaging.Contracts;
using MassTransit;

namespace FCG.Users.Infrastructure.Messaging;

public sealed class PublicadorRabbitMq(IBus bus) : IPublicadorEventos
{
    public Task PublicarAsync(UserCreatedEvent evento, CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(evento);
        return bus.Publish(evento, contexto =>
        {
            contexto.MessageId = evento.EventId;
            contexto.CorrelationId = evento.CorrelationId;
            contexto.Durable = true;
            // Confirm sozinho admite sucesso sem rota. Mandatory exige também tratar basic.return.
            contexto.Mandatory = true;
        }, tokenCancelamento);
    }
}
