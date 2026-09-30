using FCG.Users.Domain.Entities;

namespace FCG.Users.Application.Abstractions.Repositories;

public interface IRepositorioTokens
{
    Task AdicionarAsync(Token token, CancellationToken tokenCancelamento = default);

    Task<Token?> ObterPorHashAsync(
        string tokenHash,
        CancellationToken tokenCancelamento = default);

    Task<bool> TentarRotacionarAsync(
        string tokenHashAtual,
        Token novoToken,
        DateTimeOffset dataRevogacao,
        CancellationToken tokenCancelamento = default);

    Task RevogarTokensAtivosDoUsuarioAsync(
        Guid usuarioId,
        DateTimeOffset dataRevogacao,
        CancellationToken tokenCancelamento = default);
}
