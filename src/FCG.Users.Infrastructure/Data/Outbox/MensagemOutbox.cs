using System.Text.Json;
using FCG.Notifications.Application.Messaging.Contracts;
using FCG.Users.Application.Messaging;

namespace FCG.Users.Infrastructure.Data.Outbox;

// Estrutura técnica da caixa de saída; não é uma entidade do domínio de usuários.
public sealed class MensagemOutbox
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid UserId { get; private set; }
    public string EventType { get; private set; }
    public string Payload { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public int Attempts { get; private set; }

    private MensagemOutbox()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    public UserCreatedEvent LerEvento()
    {
        if (!string.Equals(EventType, DefinicaoUserCreatedEvent.Tipo, StringComparison.Ordinal))
            throw new InvalidDataException("O tipo persistido não é suportado por este publicador.");
        var evento = JsonSerializer.Deserialize<UserCreatedEvent>(Payload, JsonOptions)
            ?? throw new InvalidDataException("O payload não contém um evento.");
        ValidarEvento(evento);
        if (evento.EventId != EventId || evento.CorrelationId != CorrelationId || evento.UserId != UserId
            || evento.OccurredAt != OccurredAt)
            throw new InvalidDataException("O payload e os metadados da Outbox não representam o mesmo evento.");
        return evento;
    }

    public void RegistrarTentativa()
    {
        if (PublishedAt.HasValue) throw new InvalidOperationException("O evento já foi publicado.");
        // O contador satura no limite do tipo, sem tornar uma pendência impossível de processar.
        Attempts = (int)Math.Min((long)Attempts + 1, int.MaxValue);
    }

    public void RegistrarPublicacao(DateTimeOffset instante)
    {
        if (instante == default || instante.Offset != TimeSpan.Zero)
            throw new ArgumentException("A publicação exige um instante UTC válido.", nameof(instante));
        if (PublishedAt.HasValue) throw new InvalidOperationException("O evento já foi publicado.");
        PublishedAt = new DateTimeOffset(instante.UtcTicks - instante.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    }

    private static void ValidarEvento(UserCreatedEvent evento)
    {
        ArgumentNullException.ThrowIfNull(evento);
        if (evento.EventId == Guid.Empty || evento.CorrelationId == Guid.Empty || evento.UserId == Guid.Empty)
            throw new ArgumentException("Os identificadores do evento são obrigatórios.", nameof(evento));
        if (evento.Version != DefinicaoUserCreatedEvent.Versao)
            throw new ArgumentException("A versão do evento não é suportada.", nameof(evento));
        if (evento.OccurredAt == default || evento.OccurredAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("A ocorrência do evento deve conter um instante UTC válido.", nameof(evento));
        if (string.IsNullOrWhiteSpace(evento.Name) || string.IsNullOrWhiteSpace(evento.Email))
            throw new ArgumentException("Os dados necessários às boas-vindas são obrigatórios.", nameof(evento));
    }
    public static MensagemOutbox Criar(UserCreatedEvent evento)
    {
        ValidarEvento(evento);

        return new MensagemOutbox
        {
            Id = Guid.NewGuid(), EventId = evento.EventId, CorrelationId = evento.CorrelationId,
            UserId = evento.UserId, EventType = DefinicaoUserCreatedEvent.Tipo,
            Payload = JsonSerializer.Serialize(evento, JsonOptions), OccurredAt = evento.OccurredAt
            // PublishedAt permanece null e Attempts permanece 0; não houve envio ao broker.
        };
    }
}
