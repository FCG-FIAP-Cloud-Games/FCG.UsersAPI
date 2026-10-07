using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Domain.Entities;

namespace FCG.Users.Application.Auth;

public sealed class ManipuladorLogin
{
    private readonly IRepositoryUsuarios _repositorioUsuarios;
    private readonly IRepositorioTokens _repositorioTokens;
    private readonly IServicoHashSenha _servicoHashSenha;
    private readonly IServicoTokenJwt _servicoTokenJwt;
    private readonly IServicoRefreshToken _servicoRefreshToken;
    private readonly IUnidadeDeTrabalhoUsuarios _unidadeDeTrabalho;

    public ManipuladorLogin(
        IRepositoryUsuarios repositorioUsuarios,
        IRepositorioTokens repositorioTokens,
        IServicoHashSenha servicoHashSenha,
        IServicoTokenJwt servicoTokenJwt,
        IServicoRefreshToken servicoRefreshToken,
        IUnidadeDeTrabalhoUsuarios unidadeDeTrabalho)
    {
        _repositorioUsuarios = repositorioUsuarios;
        _repositorioTokens = repositorioTokens;
        _servicoHashSenha = servicoHashSenha;
        _servicoTokenJwt = servicoTokenJwt;
        _servicoRefreshToken = servicoRefreshToken;
        _unidadeDeTrabalho = unidadeDeTrabalho;
    }

    public async Task<ResultadoLogin> ProcessarAsync(
        ComandoLogin comando,
        CancellationToken tokenCancelamento = default)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var emailNormalizado = NormalizadorIdentidade.NormalizarEmail(comando.Email);
        var erros = Validar(comando, emailNormalizado);

        if (erros.Count > 0)
            return ResultadoLogin.DadosInvalidos(erros);

        var autenticacao = await _repositorioUsuarios.ObterAutenticacaoPorEmailAsync(
            emailNormalizado!,
            tokenCancelamento);

        if (autenticacao is null
            || !autenticacao.Usuario.Ativo
            || !_servicoHashSenha.Verificar(comando.Senha, autenticacao.Usuario.SenhaHash))
        {
            return ResultadoLogin.CredenciaisInvalidas();
        }

        return await _unidadeDeTrabalho.ExecutarAsync(autenticacao.Usuario.Id, async cancelamento =>
        {
            var atual = await _repositorioUsuarios.ObterAutenticacaoPorIdAsync(autenticacao.Usuario.Id, cancelamento);
            if (atual is null || !atual.Usuario.Ativo || atual.Usuario.Email != emailNormalizado
                || (atual.Usuario.SenhaHash != autenticacao.Usuario.SenhaHash
                    && !_servicoHashSenha.Verificar(comando.Senha, atual.Usuario.SenhaHash)))
                return ResultadoLogin.CredenciaisInvalidas();

            var accessToken = _servicoTokenJwt.GerarToken(
                atual.Usuario,
                atual.Perfil);
            var refreshToken = _servicoRefreshToken.GerarToken();

            var tokenPersistido = new Token(
                Guid.NewGuid(),
                atual.Usuario.Id,
                refreshToken.Hash,
                refreshToken.CriadoEm,
                refreshToken.ExpiraEm);

            await _repositorioTokens.AdicionarAsync(tokenPersistido, cancelamento);

            return ResultadoLogin.Autenticado(new LoginRealizado(
                accessToken.AccessToken,
                refreshToken.Valor,
                accessToken.TokenType,
                accessToken.ExpiresIn,
                accessToken.ExpiresAt,
                new UsuarioLogado(
                    atual.Usuario.Id,
                    atual.Usuario.Nome,
                    atual.Usuario.Email,
                    atual.Usuario.PerfilId,
                    atual.Perfil)));
        }, tokenCancelamento);
    }

    private static Dictionary<string, string[]> Validar(
        ComandoLogin comando,
        string? emailNormalizado)
    {
        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (emailNormalizado is null)
            erros["email"] = ["Informe um e-mail válido."];

        if (string.IsNullOrWhiteSpace(comando.Senha))
            erros["senha"] = ["Informe a senha."];

        return erros;
    }
}
