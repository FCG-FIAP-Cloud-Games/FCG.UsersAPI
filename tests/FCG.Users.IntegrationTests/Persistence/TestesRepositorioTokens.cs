using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Repositories;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FCG.Users.IntegrationTests.Persistence;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesRepositorioTokens(BancoUsersFixture banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TokenPreservaHashDe255CaracteresEInstantesUtcAoSerRecarregado()
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        var token = DadosPersistencia.NovoToken(usuario.Id, new string('h', 223) + Guid.NewGuid().ToString("N"));
        await using (var escrita = banco.CriarContexto())
            await new RepositorioTokens(escrita).AdicionarAsync(token, Cancelamento);
        await using var leitura = banco.CriarContexto();
        var carregado = await new RepositorioTokens(leitura).ObterPorHashAsync(token.TokenHash, Cancelamento);
        Assert.NotNull(carregado);
        Assert.Equal(255, carregado.TokenHash.Length);
        Assert.Equal(token.Id, carregado.Id);
        Assert.Equal(usuario.Id, carregado.UsuarioId);
        Assert.Equal(token.DataCriacao, carregado.DataCriacao);
        Assert.Equal(token.DataExpiracao, carregado.DataExpiracao);
        Assert.Equal(TimeSpan.Zero, carregado.DataCriacao.Offset);
        Assert.Null(carregado.DataRevogacao);
        Assert.Empty(leitura.ChangeTracker.Entries());
    }

    [Fact]
    public async Task RenovacoesConcorrentesPermitemSomenteUmSucessorERecusamReusoDoTokenAntigo()
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        var atual = DadosPersistencia.NovoToken(usuario.Id);
        var novoA = DadosPersistencia.NovoToken(usuario.Id);
        var novoB = DadosPersistencia.NovoToken(usuario.Id);
        await DadosPersistencia.GravarTokensAsync(banco, atual);
        await using var contextoA = banco.CriarContexto();
        await using var contextoB = banco.CriarContexto();
        var resultados = await Task.WhenAll(
            new RepositorioTokens(contextoA).TentarRotacionarAsync(atual.TokenHash, novoA, DadosPersistencia.Agora, Cancelamento),
            new RepositorioTokens(contextoB).TentarRotacionarAsync(atual.TokenHash, novoB, DadosPersistencia.Agora, Cancelamento));
        Assert.Single(resultados, resultado => resultado);
        Assert.Single(resultados, resultado => !resultado);
        await using var verificacao = banco.CriarContexto();
        var tokens = await verificacao.Tokens.AsNoTracking().Where(token => token.UsuarioId == usuario.Id)
            .ToListAsync(Cancelamento);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(DadosPersistencia.Agora, Assert.Single(tokens, token => token.Id == atual.Id).DataRevogacao);
        Assert.Single(tokens, token => token.DataRevogacao == null);
        Assert.False(await new RepositorioTokens(verificacao).TentarRotacionarAsync(
            atual.TokenHash, DadosPersistencia.NovoToken(usuario.Id), DadosPersistencia.Agora, Cancelamento));
        Assert.Equal(2, await verificacao.Tokens.CountAsync(token => token.UsuarioId == usuario.Id, Cancelamento));
    }

    [Theory]
    [InlineData("expirado")]
    [InlineData("revogado")]
    [InlineData("outroUsuario")]
    [InlineData("inexistente")]
    public async Task RotacaoRecusaTokenInvalidoSemCriarSucessor(string cenario)
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        var atual = DadosPersistencia.NovoToken(usuario.Id,
            expiracao: cenario == "expirado" ? DadosPersistencia.Agora : null);
        if (cenario == "revogado")
            atual.Revogar(DadosPersistencia.Agora.AddHours(-1));
        await DadosPersistencia.GravarTokensAsync(banco, atual);
        var donoNovo = cenario == "outroUsuario" ? await DadosPersistencia.GravarUsuarioAsync(banco) : usuario;
        var novo = DadosPersistencia.NovoToken(donoNovo.Id);
        var hashAtual = cenario == "inexistente" ? Guid.NewGuid().ToString("N") : atual.TokenHash;
        await using var contexto = banco.CriarContexto();
        Assert.False(await new RepositorioTokens(contexto).TentarRotacionarAsync(
            hashAtual, novo, DadosPersistencia.Agora, Cancelamento));
        await using var verificacao = banco.CriarContexto();
        Assert.False(await verificacao.Tokens.AnyAsync(token => token.Id == novo.Id, Cancelamento));
        Assert.Equal(atual.DataRevogacao,
            (await verificacao.Tokens.SingleAsync(token => token.Id == atual.Id, Cancelamento)).DataRevogacao);
    }

    [Fact]
    public async Task FalhaAoInserirSucessorReverteRevogacaoELimpaEntidadePendente()
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        var atual = DadosPersistencia.NovoToken(usuario.Id);
        var existente = DadosPersistencia.NovoToken(usuario.Id);
        await DadosPersistencia.GravarTokensAsync(banco, atual, existente);
        var duplicado = DadosPersistencia.NovoToken(usuario.Id, existente.TokenHash);
        await using var contexto = banco.CriarContexto();
        var repositorio = new RepositorioTokens(contexto);
        var erro = await Assert.ThrowsAsync<DbUpdateException>(() => repositorio.TentarRotacionarAsync(
            atual.TokenHash, duplicado, DadosPersistencia.Agora, Cancelamento));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(erro.InnerException).SqlState);
        Assert.Equal(EntityState.Detached, contexto.Entry(duplicado).State);
        await using (var verificacao = banco.CriarContexto())
        {
            Assert.Null((await verificacao.Tokens.SingleAsync(token => token.Id == atual.Id, Cancelamento)).DataRevogacao);
            Assert.Equal(2, await verificacao.Tokens.CountAsync(token => token.UsuarioId == usuario.Id, Cancelamento));
        }
        // A transação anterior foi revertida por inteiro; a mesma instância pode tentar uma nova operação.
        Assert.True(await repositorio.TentarRotacionarAsync(
            atual.TokenHash, DadosPersistencia.NovoToken(usuario.Id), DadosPersistencia.Agora, Cancelamento));
    }

    [Fact]
    public async Task LogoutRevogaApenasTokensAtivosDoUsuarioEPreservaRevogacoesAnteriores()
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        var outroUsuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        var ativoA = DadosPersistencia.NovoToken(usuario.Id);
        var ativoB = DadosPersistencia.NovoToken(usuario.Id);
        var expirado = DadosPersistencia.NovoToken(usuario.Id, expiracao: DadosPersistencia.Agora);
        var revogado = DadosPersistencia.NovoToken(usuario.Id);
        revogado.Revogar(DadosPersistencia.Agora.AddHours(-2));
        var deOutroUsuario = DadosPersistencia.NovoToken(outroUsuario.Id);
        await DadosPersistencia.GravarTokensAsync(banco, ativoA, ativoB, expirado, revogado, deOutroUsuario);
        await using (var escrita = banco.CriarContexto())
            await new RepositorioTokens(escrita).RevogarTokensAtivosDoUsuarioAsync(usuario.Id, DadosPersistencia.Agora, Cancelamento);
        await using var leitura = banco.CriarContexto();
        var ids = new[] { ativoA.Id, ativoB.Id, expirado.Id, revogado.Id, deOutroUsuario.Id };
        var tokens = await leitura.Tokens.Where(token => ids.Contains(token.Id)).ToDictionaryAsync(token => token.Id, Cancelamento);
        Assert.Equal(DadosPersistencia.Agora, tokens[ativoA.Id].DataRevogacao);
        Assert.Equal(DadosPersistencia.Agora, tokens[ativoB.Id].DataRevogacao);
        Assert.Null(tokens[expirado.Id].DataRevogacao);
        Assert.Equal(revogado.DataRevogacao, tokens[revogado.Id].DataRevogacao);
        Assert.Null(tokens[deOutroUsuario.Id].DataRevogacao);
    }
}
