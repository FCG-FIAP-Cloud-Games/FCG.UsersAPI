using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FCG.Users.Infrastructure.Messaging;

public sealed class PublicadorOutboxEmSegundoPlano(IServiceScopeFactory escopos, ConfiguracaoPublicadorOutbox configuracao,
    TimeProvider relogio, ILogger<PublicadorOutboxEmSegundoPlano> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Em .NET 8, o trecho anterior ao primeiro await ocorre na inicialização do host.
        await Task.Yield();
        var falhasInfraestrutura = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var espera = configuracao.Intervalo;
            try
            {
                await using var escopo = escopos.CreateAsyncScope();
                var resultado = await escopo.ServiceProvider.GetRequiredService<ProcessadorOutbox>().ProcessarLoteAsync(stoppingToken);
                falhasInfraestrutura = 0;
                if (resultado.Falhas > 0) espera = configuracao.CalcularEspera(resultado.MaiorTentativaComFalha);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception erro)
            {
                falhasInfraestrutura = Math.Min(falhasInfraestrutura + 1, 31);
                espera = configuracao.CalcularEspera(falhasInfraestrutura);
                LogsOutbox.Ciclo(logger, erro.GetType().Name, espera.TotalSeconds);
            }

            try { await Task.Delay(espera, relogio, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
        LogsOutbox.Encerrado(logger);
    }
}
