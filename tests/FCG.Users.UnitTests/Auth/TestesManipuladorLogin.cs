using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Application.Auth;
using FCG.Users.Domain.Entities;

namespace FCG.Users.UnitTests.Auth;

public sealed class TestesManipuladorLogin
{
    private static readonly DateTimeOffset Agora =
        new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessarComCredenciaisValidasRetornaTokensEPersisteSomenteHash()
    {
        var usuario = CriarUsuario();
        var repositorioTokens = new RepositorioTokensStub();
        var manipulador = CriarManipulador(usuario, repositorioTokens);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoLogin("  USUARIO@EXEMPLO.COM ", "Senha@123"),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusLogin.Sucesso, resultado.Status);
        Assert.NotNull(resultado.Login);
        Assert.Equal("access-token", resultado.Login.AccessToken);
        Assert.Equal("refresh-token-puro", resultado.Login.RefreshToken);
        Assert.Equal(PerfisSistema.Usuario, resultado.Login.Usuario.Perfil);
        Assert.NotNull(repositorioTokens.TokenAdicionado);
        Assert.Equal("HASH_DO_REFRESH_TOKEN", repositorioTokens.TokenAdicionado.TokenHash);
        Assert.DoesNotContain(
            resultado.Login.RefreshToken,
            repositorioTokens.TokenAdicionado.TokenHash,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessarComUsuarioInexistenteRetornaCredenciaisInvalidas()
    {
        var manipulador = CriarManipulador(null, new RepositorioTokensStub());

        var resultado = await manipulador.ProcessarAsync(
            new ComandoLogin("inexistente@exemplo.com", "Senha@123"),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusLogin.CredenciaisInvalidas, resultado.Status);
    }

    [Fact]
    public async Task ProcessarComSenhaIncorretaRetornaMesmoErroDoUsuarioInexistente()
    {
        var manipulador = CriarManipulador(CriarUsuario(), new RepositorioTokensStub());

        var resultado = await manipulador.ProcessarAsync(
            new ComandoLogin("usuario@exemplo.com", "Senha@Errada"),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusLogin.CredenciaisInvalidas, resultado.Status);
    }

    [Fact]
    public async Task ProcessarComUsuarioInativoNaoGeraTokens()
    {
        var usuario = CriarUsuario();
        usuario.Inativar(Agora);
        var servicoHash = new ServicoHashSenhaStub();
        var servicoJwt = new ServicoTokenJwtStub();
        var servicoRefresh = new ServicoRefreshTokenStub();
        var manipulador = new ManipuladorLogin(
            new RepositorioUsuariosStub(usuario),
            new RepositorioTokensStub(),
            servicoHash,
            servicoJwt,
            servicoRefresh);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoLogin("usuario@exemplo.com", "Senha@123"),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusLogin.CredenciaisInvalidas, resultado.Status);
        Assert.False(servicoHash.FoiChamado);
        Assert.False(servicoJwt.FoiChamado);
        Assert.False(servicoRefresh.FoiChamado);
    }

    [Fact]
    public async Task ProcessarComRequestInvalidaNaoConsultaRepositorio()
    {
        var repositorioUsuarios = new RepositorioUsuariosStub(null);
        var manipulador = CriarManipulador(
            repositorioUsuarios,
            new RepositorioTokensStub(),
            new ServicoTokenJwtStub());

        var resultado = await manipulador.ProcessarAsync(
            new ComandoLogin("email-invalido", ""),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusLogin.DadosInvalidos, resultado.Status);
        Assert.Equal(2, resultado.Erros.Count);
        Assert.False(repositorioUsuarios.FoiConsultado);
    }

    [Theory]
    [InlineData("", "Senha@123", "email")]
    [InlineData("email-invalido", "Senha@123", "email")]
    [InlineData("usuario@exemplo.com", "", "senha")]
    public async Task ProcessarComCampoInvalidoRetornaErroDoCampo(
        string email,
        string senha,
        string campo)
    {
        var repositorioUsuarios = new RepositorioUsuariosStub(null);
        var manipulador = CriarManipulador(
            repositorioUsuarios,
            new RepositorioTokensStub(),
            new ServicoTokenJwtStub());

        var resultado = await manipulador.ProcessarAsync(
            new ComandoLogin(email, senha),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusLogin.DadosInvalidos, resultado.Status);
        Assert.Contains(campo, resultado.Erros.Keys);
        Assert.False(repositorioUsuarios.FoiConsultado);
    }

    [Fact]
    public async Task ProcessarComCredenciaisValidasChamaServicosEPreservaCancellationToken()
    {
        var repositorioUsuarios = new RepositorioUsuariosStub(CriarUsuario());
        var repositorioTokens = new RepositorioTokensStub();
        var servicoHash = new ServicoHashSenhaStub();
        var servicoJwt = new ServicoTokenJwtStub();
        var servicoRefresh = new ServicoRefreshTokenStub();
        using var fonteCancelamento = new CancellationTokenSource();
        var manipulador = new ManipuladorLogin(
            repositorioUsuarios,
            repositorioTokens,
            servicoHash,
            servicoJwt,
            servicoRefresh);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoLogin("usuario@exemplo.com", "Senha@123"),
            fonteCancelamento.Token);

        Assert.Equal(StatusLogin.Sucesso, resultado.Status);
        Assert.True(servicoHash.FoiChamado);
        Assert.True(servicoJwt.FoiChamado);
        Assert.True(servicoRefresh.FoiChamado);
        Assert.Equal(fonteCancelamento.Token, repositorioUsuarios.TokenCancelamentoRecebido);
        Assert.Equal(fonteCancelamento.Token, repositorioTokens.TokenCancelamentoRecebido);
    }

    [Fact]
    public async Task ProcessarComUsuarioInexistenteNaoChamaServicosDeSeguranca()
    {
        var servicoHash = new ServicoHashSenhaStub();
        var servicoJwt = new ServicoTokenJwtStub();
        var servicoRefresh = new ServicoRefreshTokenStub();
        var manipulador = new ManipuladorLogin(
            new RepositorioUsuariosStub(null),
            new RepositorioTokensStub(),
            servicoHash,
            servicoJwt,
            servicoRefresh);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoLogin("inexistente@exemplo.com", "Senha@123"),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusLogin.CredenciaisInvalidas, resultado.Status);
        Assert.False(servicoHash.FoiChamado);
        Assert.False(servicoJwt.FoiChamado);
        Assert.False(servicoRefresh.FoiChamado);
    }

    [Fact]
    public async Task ProcessarComSenhaIncorretaValidaHashMasNaoGeraTokens()
    {
        var servicoHash = new ServicoHashSenhaStub();
        var servicoJwt = new ServicoTokenJwtStub();
        var servicoRefresh = new ServicoRefreshTokenStub();
        var manipulador = new ManipuladorLogin(
            new RepositorioUsuariosStub(CriarUsuario()),
            new RepositorioTokensStub(),
            servicoHash,
            servicoJwt,
            servicoRefresh);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoLogin("usuario@exemplo.com", "Senha@Errada"),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusLogin.CredenciaisInvalidas, resultado.Status);
        Assert.True(servicoHash.FoiChamado);
        Assert.False(servicoJwt.FoiChamado);
        Assert.False(servicoRefresh.FoiChamado);
    }

    private static Usuario CriarUsuario() =>
        new(
            Guid.NewGuid(),
            "Usuário de Teste",
            "12345678900",
            Agora.AddYears(-20),
            "usuario@exemplo.com",
            "hash::Senha@123",
            PerfisSistema.UsuarioId,
            Agora.AddDays(-1));

    private static ManipuladorLogin CriarManipulador(
        Usuario? usuario,
        RepositorioTokensStub repositorioTokens,
        ServicoTokenJwtStub? servicoJwt = null) =>
        CriarManipulador(
            new RepositorioUsuariosStub(usuario),
            repositorioTokens,
            servicoJwt ?? new ServicoTokenJwtStub());

    private static ManipuladorLogin CriarManipulador(
        RepositorioUsuariosStub repositorioUsuarios,
        RepositorioTokensStub repositorioTokens,
        ServicoTokenJwtStub servicoJwt) =>
        new(
            repositorioUsuarios,
            repositorioTokens,
            new ServicoHashSenhaStub(),
            servicoJwt,
            new ServicoRefreshTokenStub());

    private sealed class RepositorioUsuariosStub : IRepositoryUsuarios
    {
        private readonly Usuario? _usuario;

        public RepositorioUsuariosStub(Usuario? usuario)
        {
            _usuario = usuario;
        }

        public bool FoiConsultado { get; private set; }
        public CancellationToken TokenCancelamentoRecebido { get; private set; }

        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorEmailAsync(
            string email,
            CancellationToken tokenCancelamento = default)
        {
            FoiConsultado = true;
            TokenCancelamentoRecebido = tokenCancelamento;
            return Task.FromResult(
                _usuario is null
                    ? null
                    : new UsuarioAutenticacao(_usuario, PerfisSistema.Usuario));
        }

        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorIdAsync(
            Guid id,
            CancellationToken tokenCancelamento = default) =>
            Task.FromResult<UsuarioAutenticacao?>(null);

        public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken tokenCancelamento = default) =>
            Task.FromResult<Usuario?>(null);

        public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken tokenCancelamento = default) =>
            Task.FromResult(_usuario);

        public Task<bool> ExisteEmailAsync(string email, Guid? ignorarUsuarioId, CancellationToken tokenCancelamento = default) =>
            Task.FromResult(false);

        public Task<bool> ExisteCpfAsync(string cpf, Guid? ignorarUsuarioId, CancellationToken tokenCancelamento = default) =>
            Task.FromResult(false);

        public Task<bool> PerfilExisteAsync(Guid perfilId, CancellationToken tokenCancelamento = default) =>
            Task.FromResult(true);

        public Task<bool> TentarAdicionarAsync(
            Usuario usuario,
            CancellationToken tokenCancelamento = default) =>
            Task.FromResult(true);

        public Task AtualizarAsync(Usuario usuario, CancellationToken tokenCancelamento = default) =>
            Task.CompletedTask;
    }

    private sealed class RepositorioTokensStub : IRepositorioTokens
    {
        public Token? TokenAdicionado { get; private set; }
        public CancellationToken TokenCancelamentoRecebido { get; private set; }

        public Task AdicionarAsync(
            Token token,
            CancellationToken tokenCancelamento = default)
        {
            TokenAdicionado = token;
            TokenCancelamentoRecebido = tokenCancelamento;
            return Task.CompletedTask;
        }

        public Task<Token?> ObterPorHashAsync(
            string tokenHash,
            CancellationToken tokenCancelamento = default) =>
            Task.FromResult<Token?>(null);

        public Task<bool> TentarRotacionarAsync(
            string tokenHashAtual,
            Token novoToken,
            DateTimeOffset dataRevogacao,
            CancellationToken tokenCancelamento = default) =>
            Task.FromResult(false);

        public Task RevogarTokensAtivosDoUsuarioAsync(
            Guid usuarioId,
            DateTimeOffset dataRevogacao,
            CancellationToken tokenCancelamento = default) =>
            Task.CompletedTask;
    }

    private sealed class ServicoHashSenhaStub : IServicoHashSenha
    {
        public bool FoiChamado { get; private set; }

        public string GerarHash(string senha) => $"hash::{senha}";

        public bool Verificar(string senha, string senhaHash)
        {
            FoiChamado = true;
            return senhaHash == GerarHash(senha);
        }
    }

    private sealed class ServicoTokenJwtStub : IServicoTokenJwt
    {
        public bool FoiChamado { get; private set; }

        public TokenJwtGerado GerarToken(Usuario usuario, string perfil)
        {
            FoiChamado = true;
            return new TokenJwtGerado("access-token", "Bearer", 900, Agora.AddMinutes(15));
        }
    }

    private sealed class ServicoRefreshTokenStub : IServicoRefreshToken
    {
        public bool FoiChamado { get; private set; }

        public RefreshTokenGerado GerarToken()
        {
            FoiChamado = true;
            return new(
                "refresh-token-puro",
                "HASH_DO_REFRESH_TOKEN",
                Agora,
                Agora.AddDays(7));
        }

        public string CalcularHash(string token) => $"hash::{token}";
    }
}
