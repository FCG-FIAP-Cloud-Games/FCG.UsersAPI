using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Repositories;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FCG.Users.IntegrationTests.Persistence;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesAuditoriaCadastro(BancoUsersFixture banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FalhaNoLogDesfazUsuarioENaoContaminaProximaGravacaoNoMesmoContexto()
    {
        var usuario = DadosPersistencia.NovoUsuario();
        var log = LogUsuario.RegistrarCadastro(usuario.Id, usuario.CriadoEmUtc);
        await using var contexto = banco.CriarContexto();
        // O identificador provém exclusivamente de Guid.NewGuid. A constraint é local ao banco descartável.
        var sql = "ALTER TABLE \"tb_LogUsuarios\" ADD CONSTRAINT \"CK_E06_RejeitarCadastro\" CHECK (\"UsuarioId\" <> '"
            + usuario.Id.ToString("D") + "'::uuid)";
        await contexto.Database.ExecuteSqlRawAsync(sql, Cancelamento);
        try
        {
            var repositorio = new RepositorioUsuarios(contexto);
            var erro = await Assert.ThrowsAsync<DbUpdateException>(() =>
                repositorio.TentarAdicionarAsync(usuario, log, Cancelamento));
            var postgres = Assert.IsType<PostgresException>(erro.InnerException);
            Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
            Assert.Equal("CK_E06_RejeitarCadastro", postgres.ConstraintName);
            Assert.DoesNotContain(usuario.SenhaHash, erro.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(usuario.Email, erro.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(usuario.CPF, erro.ToString(), StringComparison.Ordinal);
            Assert.Empty(contexto.ChangeTracker.Entries());
            await using var verificacao = banco.CriarContexto();
            Assert.False(await verificacao.Usuarios.AnyAsync(item => item.Id == usuario.Id, Cancelamento));
            Assert.False(await verificacao.LogsUsuarios.AnyAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
            var seguinte = DadosPersistencia.NovoUsuario();
            Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await repositorio.TentarAdicionarAsync(seguinte,
                LogUsuario.RegistrarCadastro(seguinte.Id, seguinte.CriadoEmUtc), Cancelamento));
            Assert.Equal(1, await verificacao.LogsUsuarios.CountAsync(item => item.UsuarioId == seguinte.Id, Cancelamento));
        }
        finally
        {
            await contexto.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"tb_LogUsuarios\" DROP CONSTRAINT IF EXISTS \"CK_E06_RejeitarCadastro\"", CancellationToken.None);
        }
    }

    [Fact]
    public async Task RepositorioRecusaLogDeOutroUsuarioAntesDeGravar()
    {
        var usuario = DadosPersistencia.NovoUsuario();
        var logIncorreto = LogUsuario.RegistrarCadastro(Guid.NewGuid(), usuario.CriadoEmUtc);
        await using var contexto = banco.CriarContexto();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            new RepositorioUsuarios(contexto).TentarAdicionarAsync(usuario, logIncorreto, Cancelamento));
        Assert.False(await contexto.Usuarios.AnyAsync(item => item.Id == usuario.Id, Cancelamento));
        Assert.False(await contexto.LogsUsuarios.AnyAsync(item => item.Id == logIncorreto.Id, Cancelamento));
    }
}
