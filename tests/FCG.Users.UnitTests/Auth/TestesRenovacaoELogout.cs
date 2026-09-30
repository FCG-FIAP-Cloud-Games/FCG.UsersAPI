using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Application.Auth;
using FCG.Users.Domain.Entities;

namespace FCG.Users.UnitTests.Auth;

public sealed class TestesRenovacaoELogout
{
    private static readonly DateTimeOffset Agora =
        new(2026, 8, 16, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RenovarComTokenAtivoRotacionaRefreshTokenEGeraNovoJwt()
    {
        var usuario = CriarUsuario();
        var tokenAtual = CriarToken(usuario.Id);
        var repositorioTokens = new RepositorioTokensStub(tokenAtual);
        var manipulador = CriarManipulador(usuario, repositorioTokens);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoRenovarToken("refresh-atual"),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusRenovacaoToken.Sucesso, resultado.Status);
        Assert.Equal("novo-access-token", resultado.Login!.AccessToken);
        Assert.Equal("novo-refresh-token", resultado.Login.RefreshToken);
        Assert.Equal("HASH_ATUAL", repositorioTokens.HashRotacionado);
        Assert.NotNull(repositorioTokens.NovoToken);
        Assert.Equal(usuario.Id, repositorioTokens.NovoToken.UsuarioId);
    }

    [Fact]
    public async Task RenovarComTokenReutilizadoRetornaNaoAutorizadoLogico()
    {
        var usuario = CriarUsuario();
        var repositorioTokens = new RepositorioTokensStub(CriarToken(usuario.Id))
        {
            DeveRotacionar = false
        };
        var manipulador = CriarManipulador(usuario, repositorioTokens);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoRenovarToken("refresh-atual"),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusRenovacaoToken.TokenInvalido, resultado.Status);
        Assert.Null(resultado.Login);
    }

    [Fact]
    public async Task RenovarSemTokenRetornaValidacaoSemConsultarRepositorio()
    {
        var repositorioTokens = new RepositorioTokensStub(null);
        var manipulador = CriarManipulador(null, repositorioTokens);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoRenovarToken(""),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusRenovacaoToken.DadosInvalidos, resultado.Status);
        Assert.Contains("refreshToken", resultado.Erros);
        Assert.False(repositorioTokens.Consultado);
    }

    [Fact]
    public async Task LogoutRevogaTokensAtivosDoUsuarioNaDataAtual()
    {
        var repositorioTokens = new RepositorioTokensStub(null);
        var manipulador = new ManipuladorLogout(repositorioTokens, new RelogioFixo(Agora));
        var usuarioId = Guid.NewGuid();

        await manipulador.ProcessarAsync(usuarioId, TestContext.Current.CancellationToken);

        Assert.Equal(usuarioId, repositorioTokens.UsuarioRevogadoId);
        Assert.Equal(Agora, repositorioTokens.DataRevogacao);
    }


    [Theory]
    [InlineData("inexistente")]
    [InlineData("expirado")]
    [InlineData("revogado")]
    public async Task RenovarComTokenIndisponivelNaoEmiteNovaSessao(string estado)
    {
        var usuario = CriarUsuario();
        Token? token = estado switch
        {
            "inexistente" => null,
            "expirado" => new Token(Guid.NewGuid(), usuario.Id, "HASH_ATUAL", Agora.AddDays(-1), Agora),
            _ => CriarToken(usuario.Id)
        };
        if (estado == "revogado")
            token!.Revogar(Agora.AddMinutes(-1));

        var repositorio = new RepositorioTokensStub(token);
        var jwt = new ServicoTokenJwtStub();
        var resultado = await CriarManipulador(usuario, repositorio, jwt).ProcessarAsync(
            new ComandoRenovarToken("refresh-atual"), TestContext.Current.CancellationToken);

        Assert.Equal(StatusRenovacaoToken.TokenInvalido, resultado.Status);
        Assert.Null(resultado.Login);
        Assert.Null(repositorio.NovoToken);
        Assert.Null(jwt.PerfilRecebido);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenovarComUsuarioAusenteOuInativoNaoEmiteNovaSessao(bool inativo)
    {
        var usuario = CriarUsuario();
        var repositorio = new RepositorioTokensStub(CriarToken(usuario.Id));
        if (inativo)
            usuario.Inativar(Agora);
        var jwt = new ServicoTokenJwtStub();

        var resultado = await CriarManipulador(inativo ? usuario : null, repositorio, jwt)
            .ProcessarAsync(new ComandoRenovarToken("refresh-atual"), TestContext.Current.CancellationToken);

        Assert.Equal(StatusRenovacaoToken.TokenInvalido, resultado.Status);
        Assert.Null(resultado.Login);
        Assert.Null(repositorio.NovoToken);
        Assert.Null(jwt.PerfilRecebido);
    }

    [Fact]
    public async Task RenovarConsultaPerfilAtualEPreservaDadosDoUsuarioNaResposta()
    {
        var usuario = CriarUsuario();
        var repositorio = new RepositorioTokensStub(CriarToken(usuario.Id));
        var jwt = new ServicoTokenJwtStub();
        var manipulador = CriarManipulador(usuario, repositorio, jwt);
        usuario.AlterarPerfil(PerfisSistema.AdministradorId);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoRenovarToken("refresh-atual"), TestContext.Current.CancellationToken);

        Assert.Equal(StatusRenovacaoToken.Sucesso, resultado.Status);
        Assert.Equal(PerfisSistema.Administrador, jwt.PerfilRecebido);
        Assert.Equal(PerfisSistema.Administrador, resultado.Login!.Usuario.Perfil);
        Assert.Equal(PerfisSistema.AdministradorId, resultado.Login.Usuario.PerfilId);
        Assert.Equal(usuario.Nome, resultado.Login.Usuario.Nome);
        Assert.Equal(usuario.Email, resultado.Login.Usuario.Email);
        Assert.Equal(900, resultado.Login.ExpiresIn);
        Assert.Equal(Agora.AddMinutes(15), resultado.Login.ExpiresAt);
    }

    [Fact]
    public async Task RenovarUmaVezImpedeReutilizarORefreshAnterior()
    {
        var usuario = CriarUsuario();
        var tokenAnterior = CriarToken(usuario.Id);
        var repositorio = new RepositorioTokensStub(tokenAnterior);
        var manipulador = CriarManipulador(usuario, repositorio);

        var primeira = await manipulador.ProcessarAsync(
            new ComandoRenovarToken("refresh-atual"), TestContext.Current.CancellationToken);
        var repetida = await manipulador.ProcessarAsync(
            new ComandoRenovarToken("refresh-atual"), TestContext.Current.CancellationToken);

        Assert.Equal(StatusRenovacaoToken.Sucesso, primeira.Status);
        Assert.Equal(StatusRenovacaoToken.TokenInvalido, repetida.Status);
        Assert.Null(repetida.Login);
        Assert.Equal(Agora, tokenAnterior.DataRevogacao);
        Assert.True(repositorio.NovoToken!.EstaAtivo(Agora));
        Assert.Equal("NOVO_HASH", repositorio.NovoToken.TokenHash);
    }

    [Fact]
    public async Task LogoutRevogaTodasAsSessoesDoUsuarioSemRevogarOutroUsuario()
    {
        var usuario = CriarUsuario();
        var tokenAtual = CriarToken(usuario.Id);
        var segundaSessao = new Token(Guid.NewGuid(), usuario.Id, "OUTRO_HASH", Agora, Agora.AddDays(7));
        var sessaoAlheia = new Token(Guid.NewGuid(), Guid.NewGuid(), "HASH_ALHEIO", Agora, Agora.AddDays(7));
        var repositorio = new RepositorioTokensStub(tokenAtual);
        await repositorio.AdicionarAsync(segundaSessao, TestContext.Current.CancellationToken);
        await repositorio.AdicionarAsync(sessaoAlheia, TestContext.Current.CancellationToken);

        await new ManipuladorLogout(repositorio, new RelogioFixo(Agora))
            .ProcessarAsync(usuario.Id, TestContext.Current.CancellationToken);
        var renovacao = await CriarManipulador(usuario, repositorio).ProcessarAsync(
            new ComandoRenovarToken("refresh-atual"), TestContext.Current.CancellationToken);

        Assert.Equal(Agora, tokenAtual.DataRevogacao);
        Assert.Equal(Agora, segundaSessao.DataRevogacao);
        Assert.True(sessaoAlheia.EstaAtivo(Agora));
        Assert.Equal(StatusRenovacaoToken.TokenInvalido, renovacao.Status);
        Assert.Null(renovacao.Login);
    }

    [Fact]
    public async Task LogoutComUsuarioVazioNaoSolicitaRevogacao()
    {
        var repositorio = new RepositorioTokensStub(null);
        var manipulador = new ManipuladorLogout(repositorio, new RelogioFixo(Agora));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manipulador.ProcessarAsync(Guid.Empty, TestContext.Current.CancellationToken));

        Assert.Null(repositorio.UsuarioRevogadoId);
    }

    private static ManipuladorRenovarToken CriarManipulador(
        Usuario? usuario,
        RepositorioTokensStub repositorioTokens,
        ServicoTokenJwtStub? servicoJwt = null) =>
        new(
            new RepositorioUsuariosStub(usuario),
            repositorioTokens,
            servicoJwt ?? new ServicoTokenJwtStub(),
            new ServicoRefreshTokenStub(),
            new RelogioFixo(Agora));

    private static Usuario CriarUsuario() => new(
        Guid.NewGuid(),
        "Usuário",
        "12345678900",
        Agora.AddYears(-20),
        "usuario@exemplo.com",
        "hash",
        PerfisSistema.UsuarioId,
        Agora.AddDays(-1));

    private static Token CriarToken(Guid usuarioId) => new(
        Guid.NewGuid(),
        usuarioId,
        "HASH_ATUAL",
        Agora.AddDays(-1),
        Agora.AddDays(1));

    private sealed class RepositorioUsuariosStub(Usuario? usuario) : IRepositoryUsuarios
    {
        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorIdAsync(
            Guid id,
            CancellationToken token = default) =>
            Task.FromResult(
                usuario is null
                    ? null
                    : new UsuarioAutenticacao(usuario, PerfisSistema.ObterNome(usuario.PerfilId)));

        public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken token = default) => Task.FromResult<Usuario?>(null);
        public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken token = default) => Task.FromResult<Usuario?>(null);
        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorEmailAsync(string email, CancellationToken token = default) => Task.FromResult<UsuarioAutenticacao?>(null);
        public Task<bool> ExisteEmailAsync(string email, Guid? ignorarId, CancellationToken token = default) => Task.FromResult(false);
        public Task<bool> ExisteCpfAsync(string cpf, Guid? ignorarId, CancellationToken token = default) => Task.FromResult(false);
        public Task<bool> PerfilExisteAsync(Guid perfilId, CancellationToken token = default) => Task.FromResult(true);
        public Task<bool> TentarAdicionarAsync(Usuario item, CancellationToken token = default) => Task.FromResult(true);
        public Task AtualizarAsync(Usuario item, CancellationToken token = default) => Task.CompletedTask;
    }

    // Estado apenas em memória para testar a coordenação dos casos de uso; não simula concorrência SQL.
    private sealed class RepositorioTokensStub(Token? token) : IRepositorioTokens
    {
        private readonly List<Token> _tokens = token is null ? [] : [token];
        public bool DeveRotacionar { get; init; } = true;
        public bool Consultado { get; private set; }
        public string? HashRotacionado { get; private set; }
        public Token? NovoToken { get; private set; }
        public Guid? UsuarioRevogadoId { get; private set; }
        public DateTimeOffset? DataRevogacao { get; private set; }

        public Task<Token?> ObterPorHashAsync(string tokenHash, CancellationToken tokenCancelamento = default)
        {
            Consultado = true;
            return Task.FromResult(_tokens.Find(item => item.TokenHash == tokenHash));
        }

        public Task<bool> TentarRotacionarAsync(
            string tokenHashAtual,
            Token novoToken,
            DateTimeOffset dataRevogacao,
            CancellationToken tokenCancelamento = default)
        {
            HashRotacionado = tokenHashAtual;
            var atual = _tokens.Find(item => item.TokenHash == tokenHashAtual);
            if (!DeveRotacionar || atual is null || !atual.EstaAtivo(dataRevogacao))
                return Task.FromResult(false);

            atual.Revogar(dataRevogacao);
            _tokens.Add(novoToken);
            NovoToken = novoToken;
            return Task.FromResult(true);
        }

        public Task RevogarTokensAtivosDoUsuarioAsync(
            Guid usuarioId,
            DateTimeOffset dataRevogacao,
            CancellationToken tokenCancelamento = default)
        {
            UsuarioRevogadoId = usuarioId;
            DataRevogacao = dataRevogacao;
            foreach (var item in _tokens.Where(item => item.UsuarioId == usuarioId && item.EstaAtivo(dataRevogacao)))
                item.Revogar(dataRevogacao);
            return Task.CompletedTask;
        }

        public Task AdicionarAsync(Token novoToken, CancellationToken tokenCancelamento = default)
        {
            _tokens.Add(novoToken);
            return Task.CompletedTask;
        }
    }

    private sealed class ServicoTokenJwtStub : IServicoTokenJwt
    {
        public string? PerfilRecebido { get; private set; }

        public TokenJwtGerado GerarToken(Usuario usuario, string perfil)
        {
            PerfilRecebido = perfil;
            return new("novo-access-token", "Bearer", 900, Agora.AddMinutes(15));
        }
    }

    private sealed class ServicoRefreshTokenStub : IServicoRefreshToken
    {
        public RefreshTokenGerado GerarToken() =>
            new("novo-refresh-token", "NOVO_HASH", Agora, Agora.AddDays(7));

        public string CalcularHash(string token) => "HASH_ATUAL";
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
