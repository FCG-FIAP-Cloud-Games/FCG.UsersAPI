using System.Text.Json;
using FCG.Users.Infrastructure.Data;
using FCG.Users.Infrastructure.Data.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FCG.Users.Infrastructure.Messaging;

public sealed class ProcessadorOutbox(UsersDbContext contexto, IPublicadorEventos publicador,
    ConfiguracaoPublicadorOutbox configuracao, TimeProvider relogio, ILogger<ProcessadorOutbox> logger)
{
    public async Task<ResultadoLoteOutbox> ProcessarLoteAsync(CancellationToken tokenCancelamento = default)
    {
        var ids = await contexto.MensagensOutbox.AsNoTracking().Where(item => item.PublishedAt == null)
            .OrderBy(item => item.Attempts).ThenBy(item => item.OccurredAt).ThenBy(item => item.Id)
            .Select(item => item.Id).Take(configuracao.TamanhoLote).ToListAsync(tokenCancelamento);
        var tentativas = 0; var publicadas = 0; var falhas = 0; var maiorFalha = 0;
        foreach (var id in ids)
        {
            tokenCancelamento.ThrowIfCancellationRequested();
            var resultado = await ProcessarMensagemAsync(id, tokenCancelamento);
            if (resultado is null) continue; // Outra instância já reservou/concluiu esta linha.
            tentativas++;
            if (resultado.Value.Publicada) publicadas++;
            else { falhas++; maiorFalha = Math.Max(maiorFalha, resultado.Value.Tentativa); }
        }
        return new(tentativas, publicadas, falhas, maiorFalha);
    }

    private async Task<(bool Publicada, int Tentativa)?> ProcessarMensagemAsync(Guid id, CancellationToken tokenCancelamento)
    {
        await using var transacao = await contexto.Database.BeginTransactionAsync(tokenCancelamento);
        MensagemOutbox? mensagem = null;
        try
        {
            // UUID parametrizado; a reserva dura até o commit e não bloqueia quem encontra a linha ocupada.
            var registros = await contexto.MensagensOutbox.FromSqlInterpolated(
                $"SELECT * FROM \"tb_Outbox\" WHERE \"Id\" = {id} AND \"PublishedAt\" IS NULL FOR UPDATE SKIP LOCKED")
                .ToListAsync(tokenCancelamento);
            mensagem = registros.SingleOrDefault();
            if (mensagem is null) return null;
            mensagem.RegistrarTentativa();
            string? codigoFalha = null;
            FCG.Notifications.Application.Messaging.Contracts.UserCreatedEvent? evento = null;
            try { evento = mensagem.LerEvento(); }
            catch (Exception erro) when (erro is JsonException or ArgumentException or InvalidDataException)
            {
                codigoFalha = "CONTRATO_INVALIDO";
            }

            if (evento is not null)
            {
                using var prazo = CancellationTokenSource.CreateLinkedTokenSource(tokenCancelamento);
                prazo.CancelAfter(configuracao.TimeoutPublicacao);
                try
                {
                    await publicador.PublicarAsync(evento, prazo.Token).WaitAsync(prazo.Token);
                    mensagem.RegistrarPublicacao(relogio.GetUtcNow());
                }
                catch (OperationCanceledException) when (tokenCancelamento.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) { codigoFalha = "TEMPO_ESGOTADO"; }
                catch (MessageReturnedException) { codigoFalha = "SEM_ROTA"; }
                catch (Exception) { codigoFalha = "FALHA_TRANSPORTE"; }
            }

            // Publicação confirmada ou falha conhecida: persiste resultado e contador juntos.
            // Se este commit falhar, a intenção permanece; reenviar depois pode duplicar a mensagem.
            await contexto.SaveChangesAsync(tokenCancelamento);
            await transacao.CommitAsync(tokenCancelamento);
            if (codigoFalha is null)
                LogsOutbox.Publicada(logger, mensagem.EventId, mensagem.CorrelationId, mensagem.EventType, mensagem.Attempts);
            else
                LogsOutbox.Pendente(logger, mensagem.EventId, mensagem.CorrelationId, mensagem.EventType, mensagem.Attempts, codigoFalha);
            return (codigoFalha is null, mensagem.Attempts);
        }
        catch (OperationCanceledException) when (tokenCancelamento.IsCancellationRequested) { throw; }
        catch (Exception erro)
        {
            // Não incluir exception.Message/ToString: podem conter conexão ou payload pessoal.
            LogsOutbox.Persistencia(logger, mensagem?.EventId, mensagem?.CorrelationId, mensagem?.EventType, erro.GetType().Name);
            throw;
        }
        finally { contexto.ChangeTracker.Clear(); }
    }
}
