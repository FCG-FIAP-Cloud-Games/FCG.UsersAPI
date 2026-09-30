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

    public ManipuladorLogin(
        IRepositoryUsuarios repositorioUsuarios,
        IRepositorioTokens repositorioTokens,
        IServicoHashSenha servicoHashSenha,
        IServicoTokenJwt servicoTokenJwt,
        IServicoRefreshToken servicoRefreshToken)
    {
        _repositorioUsuarios = repositorioUsuarios;
        _repositorioTokens = repositorioTokens;
        _servicoHashSenha = servicoHashSenha;
        _servicoTokenJwt = servicoTokenJwt;
        _servicoRefreshToken = servicoRefreshToken;
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

        var accessToken = _servicoTokenJwt.GerarToken(
            autenticacao.Usuario,
            autenticacao.Perfil);
        var refreshToken = _servicoRefreshToken.GerarToken();

        var tokenPersistido = new Token(
            Guid.NewGuid(),
            autenticacao.Usuario.Id,
            refreshToken.Hash,
            refreshToken.CriadoEm,
            refreshToken.ExpiraEm);

        await _repositorioTokens.AdicionarAsync(tokenPersistido, tokenCancelamento);

        return ResultadoLogin.Autenticado(new LoginRealizado(
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
