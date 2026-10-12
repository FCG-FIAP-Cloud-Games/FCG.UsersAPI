using System.Security.Cryptography;
using System.Text;
using FCG.Users.Application.Abstractions.Security;

namespace FCG.Users.Infrastructure.Security;

public sealed class ServicoRefreshToken : IServicoRefreshToken
{
    private const int QuantidadeBytes = 64;
    private readonly ConfiguracaoJwt _configuracao;
    private readonly TimeProvider _relogio;

    public ServicoRefreshToken(ConfiguracaoJwt configuracao, TimeProvider relogio)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(relogio);
        _configuracao = configuracao;
        _relogio = relogio;
    }

    public RefreshTokenGerado GerarToken()
    {
        var valor = Convert.ToBase64String(RandomNumberGenerator.GetBytes(QuantidadeBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var criadoEm = _relogio.GetUtcNow();

        return new RefreshTokenGerado(
            valor,
            CalcularHash(valor),
            criadoEm,
            criadoEm.AddDays(_configuracao.RefreshTokenExpirationDays));
    }

    public string CalcularHash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
