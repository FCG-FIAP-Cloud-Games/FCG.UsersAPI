using FCG.Notifications.Application.Messaging.Contracts;

namespace FCG.Users.Infrastructure.Messaging;

// Porta técnica de transporte da Outbox. Não é chamada pelos casos de uso de cadastro.
public interface IPublicadorEventos
{
    Task PublicarAsync(UserCreatedEvent evento, CancellationToken tokenCancelamento = default);
}
