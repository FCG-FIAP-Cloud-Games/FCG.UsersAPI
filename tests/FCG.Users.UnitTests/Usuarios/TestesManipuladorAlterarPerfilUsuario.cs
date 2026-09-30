using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;

namespace FCG.Users.UnitTests.Usuarios;

public sealed class TestesManipuladorAlterarPerfilUsuario
{
    private static readonly DateTimeOffset Agora =
        new(2026, 8, 16, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessarComPerfilValidoAlteraSomentePerfil()
    {
        var usuario = CriarUsuario();
        var repositorio = new RepositorioStub(usuario);
        var manipulador = new ManipuladorAlterarPerfilUsuario(repositorio);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoAlterarPerfilUsuario(usuario.Id, PerfisSistema.AdministradorId),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusAlteracaoPerfilUsuario.Atualizado, resultado.Status);
        Assert.Equal(PerfisSistema.AdministradorId, usuario.PerfilId);
        Assert.True(repositorio.Atualizado);
    }

    [Fact]
    public async Task ProcessarComPerfilInexistenteNaoAlteraUsuario()
    {
        var usuario = CriarUsuario();
        var repositorio = new RepositorioStub(usuario) { PerfilExiste = false };
        var manipulador = new ManipuladorAlterarPerfilUsuario(repositorio);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoAlterarPerfilUsuario(usuario.Id, PerfisSistema.AdministradorId),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusAlteracaoPerfilUsuario.PerfilNaoEncontrado, resultado.Status);
        Assert.Equal(PerfisSistema.UsuarioId, usuario.PerfilId);
        Assert.False(repositorio.Atualizado);
    }

    [Fact]
    public async Task ProcessarComIdsVaziosRetornaDadosInvalidosSemConsultarRepositorio()
    {
        var repositorio = new RepositorioStub(null);
        var manipulador = new ManipuladorAlterarPerfilUsuario(repositorio);

        var resultado = await manipulador.ProcessarAsync(
            new ComandoAlterarPerfilUsuario(Guid.Empty, Guid.Empty),
            TestContext.Current.CancellationToken);

        Assert.Equal(StatusAlteracaoPerfilUsuario.DadosInvalidos, resultado.Status);
        Assert.Contains("id", resultado.Erros);
        Assert.Contains("perfilId", resultado.Erros);
        Assert.False(repositorio.Consultado);
    }

    private static Usuario CriarUsuario() => new(
        Guid.NewGuid(),
        "Usuário",
        "12345678900",
        Agora.AddYears(-20),
        "usuario@exemplo.com",
        "hash",
        PerfisSistema.UsuarioId,
        Agora.AddDays(-1));

    private sealed class RepositorioStub(Usuario? usuario) : IRepositoryUsuarios
    {
        public bool PerfilExiste { get; init; } = true;
        public bool Consultado { get; private set; }
        public bool Atualizado { get; private set; }

        public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken token = default)
        {
            Consultado = true;
            return Task.FromResult(usuario);
        }

        public Task<bool> PerfilExisteAsync(Guid perfilId, CancellationToken token = default) =>
            Task.FromResult(PerfilExiste);

        public Task AtualizarAsync(Usuario item, CancellationToken token = default)
        {
            Atualizado = true;
            return Task.CompletedTask;
        }

        public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken token = default) =>
            Task.FromResult<Usuario?>(null);

        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorEmailAsync(string email, CancellationToken token = default) =>
            Task.FromResult<UsuarioAutenticacao?>(null);

        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorIdAsync(Guid id, CancellationToken token = default) =>
            Task.FromResult<UsuarioAutenticacao?>(null);

        public Task<bool> ExisteEmailAsync(string email, Guid? ignorarId, CancellationToken token = default) =>
            Task.FromResult(false);

        public Task<bool> ExisteCpfAsync(string cpf, Guid? ignorarId, CancellationToken token = default) =>
            Task.FromResult(false);

        public Task<bool> TentarAdicionarAsync(Usuario item, CancellationToken token = default) =>
            Task.FromResult(true);
    }
}
