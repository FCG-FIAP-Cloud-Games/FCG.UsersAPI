using FCG.Users.Application.Abstractions.Repositories;

namespace FCG.Users.Application.Auth;

public sealed class ManipuladorLogout
{
    private readonly IRepositorioTokens _repositorioTokens;
    private readonly TimeProvider _relogio;

    public ManipuladorLogout(IRepositorioTokens repositorioTokens, TimeProvider relogio)
    {
        _repositorioTokens = repositorioTokens;
        _relogio = relogio;
    }

    public Task ProcessarAsync(
        Guid usuarioId,
        CancellationToken tokenCancelamento = default)
    {
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("O usuário autenticado é obrigatório.", nameof(usuarioId));

        return _repositorioTokens.RevogarTokensAtivosDoUsuarioAsync(
            usuarioId,
            _relogio.GetUtcNow(),
            tokenCancelamento);
    }
}
