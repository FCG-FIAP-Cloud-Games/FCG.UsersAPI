using System.Text.Json;
using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Usuarios;
using FCG.Users.Domain.Entities;

namespace FCG.Users.UnitTests.Usuarios;

public sealed class TestesManipuladorObterUsuario
{
    [Fact]
    public async Task ProcessarComUsuarioExistenteRetornaDadosNaoSensiveis()
    {
        var usuario = CriarUsuario();
        var resultado = await new ManipuladorObterUsuario(new RepositorioStub(usuario))
            .ProcessarAsync(new ConsultaObterUsuario(usuario.Id), TestContext.Current.CancellationToken);

        Assert.Equal(StatusObtencaoUsuario.Encontrado, resultado.Status);
        Assert.Equal(usuario.Email, resultado.Usuario!.Email);
        var json = JsonSerializer.Serialize(resultado.Usuario);
        Assert.DoesNotContain("SenhaHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CPF", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(usuario.SenhaHash, json, StringComparison.Ordinal);
        Assert.DoesNotContain(usuario.CPF, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessarComUsuarioInexistenteRetornaNaoEncontrado()
    {
        var resultado = await new ManipuladorObterUsuario(new RepositorioStub(null))
            .ProcessarAsync(new ConsultaObterUsuario(Guid.NewGuid()), TestContext.Current.CancellationToken);
        Assert.Equal(StatusObtencaoUsuario.NaoEncontrado, resultado.Status);
    }

    [Fact]
    public async Task ProcessarComIdVazioRetornaIdInvalidoSemConsultarRepositorio()
    {
        var repositorio = new RepositorioStub(null);
        var resultado = await new ManipuladorObterUsuario(repositorio)
            .ProcessarAsync(new ConsultaObterUsuario(Guid.Empty), TestContext.Current.CancellationToken);
        Assert.Equal(StatusObtencaoUsuario.IdInvalido, resultado.Status);
        Assert.False(repositorio.Consultado);
    }

    private static Usuario CriarUsuario() => new(
        Guid.NewGuid(), "Maria", "12345678900", DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime).AddYears(-20),
        "maria@exemplo.com", "hash", Guid.NewGuid(), DateTimeOffset.UtcNow);

    private sealed class RepositorioStub(Usuario? usuario) : IRepositoryUsuarios
    {
        public bool Consultado { get; private set; }
        public Task<Usuario?> ObterPorIdAsync(Guid id, CancellationToken token = default) { Consultado = true; return Task.FromResult(usuario); }
        public Task<Usuario?> ObterPorEmailAsync(string email, CancellationToken token = default) => Task.FromResult<Usuario?>(null);
        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorEmailAsync(string email, CancellationToken token = default) => Task.FromResult<UsuarioAutenticacao?>(null);
        public Task<UsuarioAutenticacao?> ObterAutenticacaoPorIdAsync(Guid id, CancellationToken token = default) => Task.FromResult<UsuarioAutenticacao?>(null);
        public Task<bool> ExisteEmailAsync(string email, Guid? ignorarId, CancellationToken token = default) => Task.FromResult(false);
        public Task<bool> ExisteCpfAsync(string cpf, Guid? ignorarId, CancellationToken token = default) => Task.FromResult(false);
        public Task<bool> PerfilExisteAsync(Guid perfilId, CancellationToken token = default) => Task.FromResult(true);
        public Task<ResultadoGravacaoUsuario> TentarAdicionarAsync(Usuario item, LogUsuario registroCadastro, CancellationToken token = default) => Task.FromResult(ResultadoGravacaoUsuario.Sucesso);
        public Task<ResultadoGravacaoUsuario> AtualizarAsync(Usuario item, CancellationToken token = default) => Task.FromResult(ResultadoGravacaoUsuario.Sucesso);
    }
}
