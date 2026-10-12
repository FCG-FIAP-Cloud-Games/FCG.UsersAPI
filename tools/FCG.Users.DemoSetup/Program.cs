using System.Runtime.InteropServices;
using Microsoft.Extensions.Configuration;

namespace FCG.Users.DemoSetup;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || args[0] != "--preparar-demonstracao")
        {
            Console.Error.WriteLine("Use somente --preparar-demonstracao no ambiente Docker de demonstração.");
            return 1;
        }

        using var cancelamento = new CancellationTokenSource();
        ConsoleCancelEventHandler aoCancelar = (_, evento) =>
        {
            evento.Cancel = true;
            cancelamento.Cancel();
        };
        Console.CancelKeyPress += aoCancelar;
        using var aoTerminar = OperatingSystem.IsLinux()
            ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, contexto =>
            {
                contexto.Cancel = true;
                cancelamento.Cancel();
            })
            : null;

        try
        {
            var configuracao = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var preparador = new PreparadorDemonstracao(configuracao);
            await preparador.ExecutarAsync(cancelamento.Token);
            return 0;
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
        {
            Console.Error.WriteLine("Preparação da demonstração cancelada.");
            return 1;
        }
        catch (Exception exception)
        {
            // Mensagens e stacks podem conter conexão, senha ou caminhos privados.
            Console.Error.WriteLine($"A preparação da demonstração falhou ({exception.GetType().Name}). Confira a configuração do ambiente e os volumes.");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= aoCancelar;
        }
    }
}
