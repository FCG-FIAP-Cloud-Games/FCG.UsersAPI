using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace FCG.Users.Infrastructure.Security;

public sealed class ConfiguracaoJwt
{
    private ConfiguracaoJwt(
        string issuer,
        string audience,
        int accessTokenExpirationMinutes,
        int refreshTokenExpirationDays,
        string activeKeyId,
        string privateKeyPath,
        IReadOnlyList<ConfiguracaoChavePublicaJwt> validationKeys)
    {
        Issuer = issuer;
        Audience = audience;
        AccessTokenExpirationMinutes = accessTokenExpirationMinutes;
        RefreshTokenExpirationDays = refreshTokenExpirationDays;
        ActiveKeyId = activeKeyId;
        PrivateKeyPath = privateKeyPath;
        ValidationKeys = validationKeys;
    }

    public string Issuer { get; }
    public string Audience { get; }
    public int AccessTokenExpirationMinutes { get; }
    public int RefreshTokenExpirationDays { get; }
    public string ActiveKeyId { get; }
    public string PrivateKeyPath { get; }
    public IReadOnlyList<ConfiguracaoChavePublicaJwt> ValidationKeys { get; }

    public static ConfiguracaoJwt Criar(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var issuer = LerTextoObrigatorio(configuration, "Jwt:Issuer");
        var audience = LerTextoObrigatorio(configuration, "Jwt:Audience");
        var minutos = LerInteiroPositivo(configuration, "Jwt:AccessTokenExpirationMinutes");
        var dias = LerInteiroPositivo(configuration, "Jwt:RefreshTokenExpirationDays");
        ValidarDuracoesRepresentaveis(minutos, dias);
        var activeKeyId = LerIdentificador(configuration, "Jwt:Signing:ActiveKeyId");
        var privateKeyPath = LerCaminho(configuration, "Jwt:Signing:PrivateKeyPath");
        var keys = new List<ConfiguracaoChavePublicaJwt>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var section in configuration.GetSection("Jwt:Validation:Keys").GetChildren())
        {
            var keyId = LerIdentificador(configuration, $"{section.Path}:KeyId");
            var publicKeyPath = LerCaminho(configuration, $"{section.Path}:PublicKeyPath");
            if (!ids.Add(keyId))
                throw new InvalidOperationException("Jwt:Validation:Keys contém identificadores de chave duplicados.");

            keys.Add(new ConfiguracaoChavePublicaJwt(keyId, publicKeyPath));
        }

        if (keys.Count == 0)
            throw new InvalidOperationException("Configure ao menos uma chave pública em Jwt:Validation:Keys.");

        if (!ids.Contains(activeKeyId))
            throw new InvalidOperationException("Jwt:Signing:ActiveKeyId deve corresponder a uma chave de Jwt:Validation:Keys.");

        return new ConfiguracaoJwt(issuer, audience, minutos, dias, activeKeyId, privateKeyPath, keys.AsReadOnly());
    }

    private static string LerTextoObrigatorio(IConfiguration configuration, string chave)
    {
        var valor = configuration[chave];
        if (string.IsNullOrWhiteSpace(valor) || !string.Equals(valor, valor.Trim(), StringComparison.Ordinal))
            throw new InvalidOperationException($"Configure {chave} com um valor não vazio, sem espaços nas extremidades.");

        return valor;
    }

    private static int LerInteiroPositivo(IConfiguration configuration, string chave)
    {
        var valor = LerTextoObrigatorio(configuration, chave);
        if (!int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var numero) || numero <= 0)
            throw new InvalidOperationException($"{chave} deve ser um número inteiro positivo.");

        return numero;
    }

    private static void ValidarDuracoesRepresentaveis(int minutos, int dias)
    {
        // Não estabelece prazo máximo de negócio: impede somente datas fora da capacidade do .NET.
        var agora = DateTimeOffset.UtcNow;
        try
        {
            _ = agora.AddMinutes(minutos);
            _ = agora.AddDays(dias);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("Os prazos de Jwt:AccessTokenExpirationMinutes e Jwt:RefreshTokenExpirationDays devem produzir datas representáveis.");
        }
    }

    private static string LerIdentificador(IConfiguration configuration, string chave)
    {
        var valor = LerTextoObrigatorio(configuration, chave);
        if (valor.Length > 128 || valor.Any(caractere => !char.IsAsciiLetterOrDigit(caractere) && caractere is not '-' and not '_' and not '.'))
            throw new InvalidOperationException($"{chave} deve conter até 128 letras ASCII, números, pontos, hífens ou sublinhados.");

        return valor;
    }

    private static string LerCaminho(IConfiguration configuration, string chave)
    {
        var valor = LerTextoObrigatorio(configuration, chave);
        if (!Path.IsPathFullyQualified(valor))
            throw new InvalidOperationException($"{chave} deve informar um caminho absoluto para um arquivo PEM externo.");

        return valor;
    }
}

public sealed class ConfiguracaoChavePublicaJwt
{
    internal ConfiguracaoChavePublicaJwt(string keyId, string publicKeyPath)
    {
        KeyId = keyId;
        PublicKeyPath = publicKeyPath;
    }

    public string KeyId { get; }
    public string PublicKeyPath { get; }
}
