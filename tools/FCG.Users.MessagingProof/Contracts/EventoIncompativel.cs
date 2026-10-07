namespace FCG.Users.MessagingProof.Incompativel;

// Mesmos campos; namespace diferente: identidade de transporte diferente.
public sealed record UserCreatedEvent(
    Guid EventId,
    Guid CorrelationId,
    DateTimeOffset OccurredAt,
    int Version,
    Guid UserId,
    string Name,
    string Email);
