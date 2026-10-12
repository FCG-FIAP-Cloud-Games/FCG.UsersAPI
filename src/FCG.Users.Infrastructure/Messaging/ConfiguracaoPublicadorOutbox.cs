using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace FCG.Users.Infrastructure.Messaging;

public sealed class ConfiguracaoPublicadorOutbox
{
    private ConfiguracaoPublicadorOutbox(bool habilitado, int tamanhoLote, TimeSpan intervalo,
        TimeSpan timeoutPublicacao, TimeSpan retryInicial, TimeSpan retryMaximo)
    {
        Habilitado = habilitado; TamanhoLote = tamanhoLote; Intervalo = intervalo;
        TimeoutPublicacao = timeoutPublicacao; RetryInicial = retryInicial; RetryMaximo = retryMaximo;
    }

    public bool Habilitado { get; }
    public int TamanhoLote { get; }
    public TimeSpan Intervalo { get; }
    public TimeSpan TimeoutPublicacao { get; }
    public TimeSpan RetryInicial { get; }
    public TimeSpan RetryMaximo { get; }

    public static ConfiguracaoPublicadorOutbox Criar(IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        var habilitado = LerBooleano(configuracao, "Outbox:Enabled");
        if (!habilitado)
            return new(false, 20, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(300));

        var lote = configuracao["Outbox:BatchSize"];
        var tamanho = 20;
        if (lote is not null && (!int.TryParse(lote, NumberStyles.None, CultureInfo.InvariantCulture, out tamanho) || tamanho is < 1 or > 100))
            throw new InvalidOperationException("Outbox:BatchSize deve conter um inteiro entre 1 e 100.");
        var inicial = LerDuracao(configuracao, "Outbox:InitialRetrySeconds", 5, 3600);
        var maximo = LerDuracao(configuracao, "Outbox:MaxRetrySeconds", 300, 86400);
        if (maximo < inicial)
            throw new InvalidOperationException("Outbox:MaxRetrySeconds deve ser maior ou igual a Outbox:InitialRetrySeconds.");

        return new(true, tamanho, LerDuracao(configuracao, "Outbox:PollIntervalSeconds", 2, 60),
            LerDuracao(configuracao, "Outbox:PublishTimeoutSeconds", 10, 60), inicial, maximo);
    }

    public TimeSpan CalcularEspera(int tentativas)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tentativas);
        var segundos = Math.Min(RetryMaximo.TotalSeconds,
            RetryInicial.TotalSeconds * Math.Pow(2, Math.Min(tentativas - 1, 30)));
        return TimeSpan.FromSeconds(segundos);
    }

    internal static bool LerBooleano(IConfiguration configuracao, string chave)
    {
        var valor = configuracao[chave];
        if (valor is null) return false;
        if (!bool.TryParse(valor, out var resultado))
            throw new InvalidOperationException($"{chave} deve conter true ou false.");
        return resultado;
    }

    private static TimeSpan LerDuracao(IConfiguration configuracao, string chave, double padrao, double maximo)
    {
        var valor = configuracao[chave];
        if (valor is null) return TimeSpan.FromSeconds(padrao);
        if (!double.TryParse(valor, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var segundos)
            || !double.IsFinite(segundos) || segundos < 0.1 || segundos > maximo)
            throw new InvalidOperationException($"{chave} deve conter segundos entre 0.1 e {maximo.ToString(CultureInfo.InvariantCulture)}, usando ponto decimal.");
        return TimeSpan.FromSeconds(segundos);
    }
}
