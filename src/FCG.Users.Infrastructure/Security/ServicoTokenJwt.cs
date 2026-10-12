using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Domain.Entities;

namespace FCG.Users.Infrastructure.Security;

public sealed class ServicoTokenJwt : IServicoTokenJwt
{
    private readonly ConfiguracaoJwt _configuracao;
    private readonly ChavesJwtRsa _chaves;
    private readonly TimeProvider _relogio;

    public ServicoTokenJwt(ConfiguracaoJwt configuracao, ChavesJwtRsa chaves, TimeProvider relogio)
    {
        ArgumentNullException.ThrowIfNull(configuracao);
        ArgumentNullException.ThrowIfNull(chaves);
        ArgumentNullException.ThrowIfNull(relogio);
        _configuracao = configuracao;
        _chaves = chaves;
        _relogio = relogio;
    }

    public TokenJwtGerado GerarToken(Usuario usuario, string perfil)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentException.ThrowIfNullOrWhiteSpace(perfil);
        if (usuario.Id == Guid.Empty || !usuario.Ativo)
            throw new InvalidOperationException("Somente usuários válidos e ativos podem receber um access token.");
        if (perfil is not PerfisSistema.Usuario and not PerfisSistema.Administrador)
            throw new InvalidOperationException("O perfil do usuário não é aceito para emissão de access token.");

        // JWT usa segundos inteiros desde a época Unix: a resposta deve apresentar a mesma expiração.
        var emitidoEm = DateTimeOffset.FromUnixTimeSeconds(_relogio.GetUtcNow().ToUnixTimeSeconds());
        var expiraEm = emitidoEm.AddMinutes(_configuracao.AccessTokenExpirationMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id.ToString()),
            new Claim("role", perfil),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, emitidoEm.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };
        var jwt = new JwtSecurityToken(
            _configuracao.Issuer,
            _configuracao.Audience,
            claims,
            emitidoEm.UtcDateTime,
            expiraEm.UtcDateTime,
            _chaves.CriarCredenciaisAssinatura());

        return new TokenJwtGerado(
            new JwtSecurityTokenHandler().WriteToken(jwt),
            "Bearer",
            (long)(expiraEm - emitidoEm).TotalSeconds,
            expiraEm);
    }
}
