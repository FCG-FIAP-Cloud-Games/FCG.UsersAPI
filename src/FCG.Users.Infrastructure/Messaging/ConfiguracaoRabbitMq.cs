using Microsoft.Extensions.Configuration;

namespace FCG.Users.Infrastructure.Messaging;

public sealed class ConfiguracaoRabbitMq
{
    private ConfiguracaoRabbitMq(string host, ushort port, string virtualHost, string username, string password, bool useTls)
    {
        Host = host; Port = port; VirtualHost = virtualHost; Username = username; Password = password; UseTls = useTls;
    }

    public string Host { get; }
    public ushort Port { get; }
    public string VirtualHost { get; }
    public string Username { get; }
    public string Password { get; }
    public bool UseTls { get; }

    public static ConfiguracaoRabbitMq Criar(IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        var host = LerTexto(configuracao, "RabbitMq:Host");
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new InvalidOperationException("RabbitMq:Host deve conter somente o nome ou endereço do servidor, sem URL ou credenciais.");
        var tls = ConfiguracaoPublicadorOutbox.LerBooleano(configuracao, "RabbitMq:UseTls");
        var porta = (ushort)(tls ? 5671 : 5672);
        var valorPorta = configuracao["RabbitMq:Port"];
        if (valorPorta is not null && (!ushort.TryParse(valorPorta, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out porta) || porta == 0))
            throw new InvalidOperationException("RabbitMq:Port deve ser um inteiro entre 1 e 65535.");
        var password = configuracao["RabbitMq:Password"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Configure RabbitMq:Password externamente, sem credencial fixa no código.");
        return new(host, porta, LerTexto(configuracao, "RabbitMq:VirtualHost"),
            LerTexto(configuracao, "RabbitMq:Username"), password, tls);
    }

    private static string LerTexto(IConfiguration configuracao, string chave)
    {
        var valor = configuracao[chave];
        if (string.IsNullOrWhiteSpace(valor) || !string.Equals(valor, valor.Trim(), StringComparison.Ordinal)
            || valor.Any(char.IsControl))
            throw new InvalidOperationException($"Configure {chave} com um valor não vazio, sem espaços nas extremidades ou caracteres de controle.");
        return valor;
    }
}
