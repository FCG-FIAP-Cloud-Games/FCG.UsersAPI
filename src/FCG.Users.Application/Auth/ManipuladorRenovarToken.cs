using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Domain.Entities;

namespace FCG.Users.Application.Auth;

public sealed class ManipuladorRenovarToken
{
    private readonly IRepositoryUsuarios _repositorioUsuarios;
    private readonly IRepositorioTokens _repositorioTokens;
    private readonly IServicoTokenJwt _servicoTokenJwt;
    private readonly IServicoRefreshToken _servicoRefreshToken;
    private readonly TimeProvider _relogio;
    private readonly IUnidadeDeTrabalhoUsuarios _unidadeDeTrabalho;

    public ManipuladorRenovarToken(
        IRepositoryUsuarios repositorioUsuarios,
        IRepositorioTokens repositorioTokens,
        IServicoTokenJwt servicoTokenJwt,
        IServicoRefreshToken servicoRefreshToken,
        TimeProvider relogio,
        IUnidadeDeTrabalhoUsuarios unidadeDeTrabalho)
    {
        _repositorioUsuarios = repositorioUsuarios;
        _repositorioTokens = repositorioTokens;
        _servicoTokenJwt = servicoTokenJwt;
        _servicoRefreshToken = servicoRefreshToken;
        _relogio = relogio;
        _unidadeDeTrabalho = unidadeDeTrabalho;
    }

    public async Task<ResultadoRenovarToken> ProcessarAsync(
        ComandoRenovarToken comando,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        if (string.IsNullOrWhiteSpace(comando.RefreshToken))
        {
            return ResultadoRenovarToken.DadosInvalidos(
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["refreshToken"] = ["Informe o refresh token."]
                });
        }

        var agora = _relogio.GetUtcNow();
        var tokenHash = _servicoRefreshToken.CalcularHash(comando.RefreshToken);
        var tokenAtual = await _repositorioTokens.ObterPorHashAsync(tokenHash, tokenCancelamento);

        if (tokenAtual is null || !tokenAtual.EstaAtivo(agora))
            return ResultadoRenovarToken.TokenInvalido();

        return await _unidadeDeTrabalho.ExecutarAsync(tokenAtual.UsuarioId, async cancelamento =>
        {
            var autenticacao = await _repositorioUsuarios.ObterAutenticacaoPorIdAsync(
                tokenAtual.UsuarioId,
                cancelamento);

            if (autenticacao is null || !autenticacao.Usuario.Ativo)
                return ResultadoRenovarToken.TokenInvalido();

            var accessToken = _servicoTokenJwt.GerarToken(
                autenticacao.Usuario,
                autenticacao.Perfil);
            var refreshToken = _servicoRefreshToken.GerarToken();
            var novoToken = new Token(
                Guid.NewGuid(),
                autenticacao.Usuario.Id,
                refreshToken.Hash,
                refreshToken.CriadoEm,
                refreshToken.ExpiraEm);

            var rotacionado = await _repositorioTokens.TentarRotacionarAsync(
                tokenHash,
                novoToken,
                _relogio.GetUtcNow(),
                cancelamento);

            if (!rotacionado)
                return ResultadoRenovarToken.TokenInvalido();

            return ResultadoRenovarToken.Renovado(new LoginRealizado(
                accessToken.AccessToken,
                refreshToken.Valor,
                accessToken.TokenType,
                accessToken.ExpiresIn,
                accessToken.ExpiresAt,
                new UsuarioLogado(
                    autenticacao.Usuario.Id,
                    autenticacao.Usuario.Nome,
                    autenticacao.Usuario.Email,
                    autenticacao.Usuario.PerfilId,
                    autenticacao.Perfil)));
        }, tokenCancelamento);
    }
}
