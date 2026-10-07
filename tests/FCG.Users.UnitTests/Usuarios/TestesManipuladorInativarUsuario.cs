using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;
using FCG.Users.UnitTests.Support;

namespace FCG.Users.UnitTests.Usuarios;

public sealed class TestesManipuladorInativarUsuario
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InativacaoUsaMesmoInstanteNaEntidadeELogPreservandoDados()
    {
        var usuario = CriarUsuario();
        var repositorio = new RepositorioTeste(usuario);
        var resultado = await Criar(repositorio).ProcessarAsync(new(usuario.Id), TestContext.Current.CancellationToken);
        Assert.Equal(StatusInativacaoUsuario.Inativado, resultado);
        Assert.False(usuario.Ativo);
        Assert.Equal(Agora, usuario.DataInativacao);
        Assert.NotNull(repositorio.Auditoria);
        Assert.Equal(usuario.Id, repositorio.Auditoria.UsuarioId);
        Assert.Equal(Agora, repositorio.Auditoria.DataCriacao);
        Assert.Equal("Usuário inativado.", repositorio.Auditoria.Descricao);
        Assert.Equal("Pessoa Teste", usuario.Nome);
        Assert.Equal("hash", usuario.SenhaHash);
    }

    [Fact]
    public async Task RepetirInativacaoPreservaInstanteOriginalSemNovaGravacao()
    {
        var usuario = CriarUsuario();
        var anterior = Agora.AddDays(-1);
        usuario.Inativar(anterior);
        var repositorio = new RepositorioTeste(usuario);
        Assert.Equal(StatusInativacaoUsuario.Inativado,
            await Criar(repositorio).ProcessarAsync(new(usuario.Id), TestContext.Current.CancellationToken));
        Assert.Equal(anterior, usuario.DataInativacao);
        Assert.Null(repositorio.Auditoria);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IdVazioOuUsuarioAusenteNaoGrava(bool vazio)
    {
        var repositorio = new RepositorioTeste(null);
        var resultado = await Criar(repositorio).ProcessarAsync(new(vazio ? Guid.Empty : Guid.NewGuid()), TestContext.Current.CancellationToken);
        Assert.Equal(vazio ? StatusInativacaoUsuario.IdInvalido : StatusInativacaoUsuario.NaoEncontrado, resultado);
        Assert.Equal(!vazio, repositorio.Consultado);
        Assert.Null(repositorio.Auditoria);
    }

    [Theory]
    [InlineData(ResultadoGravacaoUsuario.ConflitoEmail)]
    [InlineData(ResultadoGravacaoUsuario.ConflitoCpf)]
    public async Task FalhaInesperadaNaoProduzSucesso(ResultadoGravacaoUsuario gravacao)
    {
        var usuario = CriarUsuario();
        var repositorio = new RepositorioTeste(usuario) { Resultado = gravacao };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Criar(repositorio).ProcessarAsync(new(usuario.Id), TestContext.Current.CancellationToken));
    }

    private static Usuario CriarUsuario() => new(Guid.NewGuid(), "Pessoa Teste", "12345678900",
        new DateOnly(2000, 1, 1), "pessoa@example.test", "hash", PerfisSistema.UsuarioId, Agora.AddDays(-3));
    private static ManipuladorInativarUsuario Criar(RepositorioTeste repositorio) =>
        new(repositorio, new RelogioFixo(), new UnidadeDeTrabalhoTeste());
    private sealed class RelogioFixo : TimeProvider { public override DateTimeOffset GetUtcNow() => Agora; }
    private sealed class RepositorioTeste(Usuario? usuario) : IRepositoryUsuarios
    {
        public bool Consultado { get; private set; }
        public LogUsuario? Auditoria { get; private set; }
        public ResultadoGravacaoUsuario Resultado { get; init; } = ResultadoGravacaoUsuario.Sucesso;
        public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken token = default) { Consultado = true; return Task.FromResult(usuario); }
        public Task<ResultadoGravacaoUsuario> AtualizarAsync(Usuario item, LogUsuario registroAuditoria, CancellationToken token = default)
        { Auditoria = registroAuditoria; return Task.FromResult(Resultado); }
        public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken token = default) => Task.FromResult<Usuario?>(null);
        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorEmailAsync(string email, CancellationToken token = default) => Task.FromResult<UsuarioAutenticacao?>(null);
        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorIdAsync(Guid id, CancellationToken token = default) => Task.FromResult<UsuarioAutenticacao?>(null);
        public Task<bool> ExisteEmailAsync(string email, Guid? ignorar, CancellationToken token = default) => Task.FromResult(false);
        public Task<bool> ExisteCpfAsync(string cpf, Guid? ignorar, CancellationToken token = default) => Task.FromResult(false);
        public Task<bool> PerfilExisteAsync(Guid perfil, CancellationToken token = default) => Task.FromResult(true);
        public Task<ResultadoGravacaoUsuario> TentarAdicionarAsync(Usuario item, LogUsuario log, CancellationToken token = default) => Task.FromResult(ResultadoGravacaoUsuario.Sucesso);
    }
}
