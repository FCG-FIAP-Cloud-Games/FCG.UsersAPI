// Identidade preservada para a prova com Notifications, commit 78b5124.
// Não há referência de projeto nem dependência binária da NotificationsAPI.
namespace FCG.Notifications.Application.Messaging.Contracts;

public sealed record UserCreatedEvent(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int Version,
    Guid UserId,
    string Name,
    string Email);
