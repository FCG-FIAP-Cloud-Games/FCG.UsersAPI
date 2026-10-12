using Microsoft.Extensions.Logging;

namespace FCG.Users.Infrastructure.Messaging;

internal static partial class LogsOutbox
{
    [LoggerMessage(EventId = 1201, Level = LogLevel.Information,
        Message = "Outbox publicada: EventId {EventId}, CorrelationId {CorrelationId}, EventType {EventType}, tentativa {Attempts}.")]
    internal static partial void Publicada(ILogger logger, Guid eventId, Guid correlationId, string eventType, int attempts);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Warning,
        Message = "Outbox pendente: EventId {EventId}, CorrelationId {CorrelationId}, EventType {EventType}, tentativa {Attempts}, código {ErrorCode}.")]
    internal static partial void Pendente(ILogger logger, Guid eventId, Guid correlationId, string eventType, int attempts, string errorCode);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Error,
        Message = "Falha de persistência da Outbox: EventId {EventId}, CorrelationId {CorrelationId}, EventType {EventType}, tipo {ErrorType}.")]
    internal static partial void Persistencia(ILogger logger, Guid? eventId, Guid? correlationId, string? eventType, string errorType);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Error,
        Message = "Ciclo Outbox interrompido: tipo {ErrorType}. Nova tentativa após {RetrySeconds} segundos.")]
    internal static partial void Ciclo(ILogger logger, string errorType, double retrySeconds);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Information, Message = "Publicador Outbox encerrado.")]
    internal static partial void Encerrado(ILogger logger);
}
