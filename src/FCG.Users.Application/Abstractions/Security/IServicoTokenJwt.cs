using FCG.Users.Domain.Entities;

namespace FCG.Users.Application.Abstractions.Security;

public interface IServicoTokenJwt
{
    TokenJwtGerado GerarToken(Usuario usuario, string perfil);
}

public sealed record TokenJwtGerado(
    string AccessToken,
    string TokenType,
    long ExpiresIn,
    DateTimeOffset ExpiresAt);
