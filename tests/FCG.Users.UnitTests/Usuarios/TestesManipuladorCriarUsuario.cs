using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Abstractions.Security;
using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;

namespace FCG.Users.UnitTests.Usuarios;

public sealed class TestesManipuladorCriarUsuario
{
    private static readonly DateTimeOffset Agora = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PerfilId = Guid.Parse("4f642cbc-3720-4bb2-b456-15a97049da5c");

    [Fact]
    public async Task ProcessarComDadosValidosNormalizaEProtegeDadosAntesDePersistir()
    {
        var repositorio = new RepositorioUsuariosStub();
        var resultado = await CriarManipulador(repositorio).ProcessarAsync(
            CriarComando(), TestContext.Current.CancellationToken);

        Assert.Equal(StatusCriacaoUsuario.Criado, resultado.Status);
        Assert.Equal("Maria da Silva", resultado.Usuario!.Nome);
        Assert.Equal("maria@exemplo.com", resultado.Usuario.Email);
        Assert.Equal(Agora, resultado.Usuario.CriadoEmUtc);
        Assert.Equal("12345678900", repositorio.UsuarioAdicionado!.CPF);
        Assert.Equal("hash-protegido", repositorio.UsuarioAdicionado.SenhaHash);
        Assert.NotEqual("Senha@123", repositorio.UsuarioAdicionado.SenhaHash);
        Assert.All(
            new[] { repositorio.UsuarioAdicionado.Nome, repositorio.UsuarioAdicionado.CPF, repositorio.UsuarioAdicionado.Email, repositorio.UsuarioAdicionado.SenhaHash },
            valor => Assert.False(string.IsNullOrWhiteSpace(valor)));
    }

    [Fact]
    public async Task ProcessarComComandoNuloRejeitaRequest()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            CriarManipulador(new RepositorioUsuariosStub()).ProcessarAsync(
                null!, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("", "12345678900", "maria@exemplo.com", "Senha@123", "nome")]
    [InlineData("Maria da Silva", "", "maria@exemplo.com", "Senha@123", "cpf")]
    [InlineData("Maria da Silva", "12345678900", "", "Senha@123", "email")]
    [InlineData("Maria da Silva", "12345678900", "email-invalido", "Senha@123", "email")]
    [InlineData("Maria da Silva", "12345678900", "maria@exemplo.com", "", "senha")]
    public async Task ProcessarComCampoInvalidoRetornaErroDoCampo(
        string nome, string cpf, string email, string senha, string campo)
    {
        var repositorio = new RepositorioUsuariosStub();
        var comando = new ComandoCriarUsuario(nome, cpf, Agora.AddYears(-20), email, senha, PerfilId);

        var resultado = await CriarManipulador(repositorio).ProcessarAsync(
            comando, TestContext.Current.CancellationToken);

        Assert.Equal(StatusCriacaoUsuario.DadosInvalidos, resultado.Status);
        Assert.Contains(campo, resultado.Erros);
        Assert.Null(repositorio.UsuarioAdicionado);
    }

    [Fact]
    public async Task ProcessarComDataNascimentoFuturaRetornaErro()
    {
        var comando = CriarComando() with { DataNascimento = Agora.AddDays(1) };
        var resultado = await CriarManipulador(new RepositorioUsuariosStub()).ProcessarAsync(comando, TestContext.Current.CancellationToken);
        Assert.Contains("dataNascimento", resultado.Erros);
    }

    [Fact]
    public async Task ProcessarComPerfilVazioRetornaErro()
    {
        var resultado = await CriarManipulador(new RepositorioUsuariosStub()).ProcessarAsync(
            CriarComando() with { PerfilId = Guid.Empty }, TestContext.Current.CancellationToken);
        Assert.Contains("perfilId", resultado.Erros);
    }

    [Fact]
    public async Task ProcessarComPerfilInexistenteRetornaResultadoEsperado()
    {
        var repositorio = new RepositorioUsuariosStub { PerfilExiste = false };
        var resultado = await CriarManipulador(repositorio).ProcessarAsync(CriarComando(), TestContext.Current.CancellationToken);
        Assert.Equal(StatusCriacaoUsuario.PerfilNaoEncontrado, resultado.Status);
        Assert.Null(repositorio.UsuarioAdicionado);
    }

    [Fact]
    public async Task ProcessarComEmailDuplicadoRetornaConflito()
    {
        var resultado = await CriarManipulador(new RepositorioUsuariosStub { EmailExiste = true })
            .ProcessarAsync(CriarComando(), TestContext.Current.CancellationToken);
        Assert.Equal(StatusCriacaoUsuario.EmailJaCadastrado, resultado.Status);
    }

    [Fact]
    public async Task ProcessarComCpfDuplicadoRetornaConflito()
    {
        var resultado = await CriarManipulador(new RepositorioUsuariosStub { CpfExiste = true })
            .ProcessarAsync(CriarComando(), TestContext.Current.CancellationToken);
        Assert.Equal(StatusCriacaoUsuario.CpfJaCadastrado, resultado.Status);
    }

    private static ComandoCriarUsuario CriarComando() => new(
        "  Maria   da Silva  ", "123.456.789-00", Agora.AddYears(-20),
        "  MARIA@EXEMPLO.COM  ", "Senha@123", PerfilId);

    private static ManipuladorCriarUsuario CriarManipulador(RepositorioUsuariosStub repositorio) =>
        new(repositorio, new HashSenhaStub(), new RelogioFixo(Agora));

    private sealed class RepositorioUsuariosStub : IRepositoryUsuarios
    {
        public bool PerfilExiste { get; init; } = true;
        public bool EmailExiste { get; init; }
        public bool CpfExiste { get; init; }
        public Usuario? UsuarioAdicionado { get; private set; }
        public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken token = default) => Task.FromResult<Usuario?>(null);
        public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken token = default) => Task.FromResult<Usuario?>(null);
        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorEmailAsync(string email, CancellationToken token = default) => Task.FromResult<UsuarioAutenticacao?>(null);
        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorIdAsync(Guid id, CancellationToken token = default) => Task.FromResult<UsuarioAutenticacao?>(null);
        public Task<bool> ExisteEmailAsync(string email, Guid? ignorarId, CancellationToken token = default) => Task.FromResult(EmailExiste);
        public Task<bool> ExisteCpfAsync(string cpf, Guid? ignorarId, CancellationToken token = default) => Task.FromResult(CpfExiste);
        public Task<bool> PerfilExisteAsync(Guid perfilId, CancellationToken token = default) => Task.FromResult(PerfilExiste);
        public Task<bool> TentarAdicionarAsync(Usuario usuario, CancellationToken token = default)
        {
            UsuarioAdicionado = usuario;
            return Task.FromResult(true);
        }
        public Task AtualizarAsync(Usuario usuario, CancellationToken token = default) => Task.CompletedTask;
    }

    private sealed class HashSenhaStub : IHashSenha
    {
        public string Criar(string senha) => "hash-protegido";
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
