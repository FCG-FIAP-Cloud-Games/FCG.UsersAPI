using FCG.Users.Domain.Entities;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FCG.Users.IntegrationTests.Persistence;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesEstruturaBanco(BancoUsersFixture banco)
{
    private static readonly string[] TabelasEsperadas =
        ["__EFMigrationsHistory", "tb_LogUsuarios", "tb_Perfil", "tb_Tokens", "tb_Usuarios"];
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigrationCriaSomenteTabelasDoServicoEPerfisComIdentificadoresEstaveis()
    {
        await using var contexto = banco.CriarContexto();
        var tabelas = await contexto.Database.SqlQueryRaw<string>(
            "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'")
            .ToListAsync(Cancelamento);

        Assert.Equal(TabelasEsperadas,
            tabelas.Order(StringComparer.Ordinal));
        Assert.Single(await contexto.Database.GetAppliedMigrationsAsync(Cancelamento));
        Assert.Empty(await contexto.Database.GetPendingMigrationsAsync(Cancelamento));
        var perfis = await contexto.Perfis.ToDictionaryAsync(perfil => perfil.Id, perfil => perfil.Nome, Cancelamento);
        Assert.Equal(2, perfis.Count);
        Assert.Equal(PerfisSistema.Usuario, perfis[PerfisSistema.UsuarioId]);
        Assert.Equal(PerfisSistema.Administrador, perfis[PerfisSistema.AdministradorId]);
    }

    [Fact]
    public async Task MigrationUsaDateParaNascimentoTimestamptzParaInstantesELimitesDoPdf()
    {
        await using var contexto = banco.CriarContexto();
        var colunas = await contexto.Database.SqlQueryRaw<string>(
            "SELECT table_name || '.' || column_name || ':' || data_type || ':' || COALESCE(character_maximum_length::text, '-') AS \"Value\" FROM information_schema.columns WHERE table_schema = 'public'")
            .ToListAsync(Cancelamento);

        Assert.Contains("tb_Usuarios.DataNascimento:date:-", colunas);
        Assert.Contains("tb_Usuarios.CriadoEmUtc:timestamp with time zone:-", colunas);
        Assert.Contains("tb_Usuarios.DataInativacao:timestamp with time zone:-", colunas);
        Assert.Contains("tb_Usuarios.CPF:character varying:11", colunas);
        foreach (var coluna in new[] { "tb_Usuarios.Nome", "tb_Usuarios.Email", "tb_Usuarios.SenhaHash",
                     "tb_Perfil.Nome", "tb_Tokens.TokenHash", "tb_LogUsuarios.Descricao" })
            Assert.Contains($"{coluna}:character varying:255", colunas);
        foreach (var coluna in new[] { "tb_Tokens.DataCriacao", "tb_Tokens.DataExpiracao",
                     "tb_Tokens.DataRevogacao", "tb_LogUsuarios.DataCriacao" })
            Assert.Contains($"{coluna}:timestamp with time zone:-", colunas);
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("1234567890a")]
    [InlineData("１２３４５６７８９０１")]
    public async Task BancoRejeitaCpfForaDoFormatoMesmoSemPassarPelaAplicacao(string cpf)
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        await using var contexto = banco.CriarContexto();
        var erro = await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"tb_Usuarios\" SET \"CPF\" = {cpf} WHERE \"Id\" = {usuario.Id}", Cancelamento));
        Assert.Equal(PostgresErrorCodes.CheckViolation, erro.SqlState);
        Assert.Equal("CK_tb_Usuarios_CPF_Formato", erro.ConstraintName);
    }

    [Theory]
    [InlineData("Nome@exemplo.test")]
    [InlineData(" nome@exemplo.test ")]
    public async Task BancoRejeitaEmailNaoNormalizadoMesmoSemPassarPelaAplicacao(string email)
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        await using var contexto = banco.CriarContexto();
        var erro = await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"tb_Usuarios\" SET \"Email\" = {email} WHERE \"Id\" = {usuario.Id}", Cancelamento));
        Assert.Equal(PostgresErrorCodes.CheckViolation, erro.SqlState);
        Assert.Equal("CK_tb_Usuarios_Email_Normalizado", erro.ConstraintName);
    }

    [Fact]
    public async Task BancoRejeitaHashAcimaDe255CaracteresENomeNulo()
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        await using var contexto = banco.CriarContexto();
        var hashLongo = new string('h', 256);
        var tamanho = await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"tb_Usuarios\" SET \"SenhaHash\" = {hashLongo} WHERE \"Id\" = {usuario.Id}", Cancelamento));
        Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, tamanho.SqlState);
        var obrigatorio = await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"tb_Usuarios\" SET \"Nome\" = NULL WHERE \"Id\" = {usuario.Id}", Cancelamento));
        Assert.Equal(PostgresErrorCodes.NotNullViolation, obrigatorio.SqlState);
    }

    [Theory]
    [InlineData("perfil")]
    [InlineData("token")]
    [InlineData("log")]
    public async Task BancoRejeitaReferenciasInexistentes(string referencia)
    {
        await using var contexto = banco.CriarContexto();
        switch (referencia)
        {
            case "perfil": contexto.Usuarios.Add(DadosPersistencia.NovoUsuario(perfilId: Guid.NewGuid())); break;
            case "token": contexto.Tokens.Add(DadosPersistencia.NovoToken(Guid.NewGuid())); break;
            case "log": contexto.LogsUsuarios.Add(LogUsuario.RegistrarCadastro(Guid.NewGuid(), DadosPersistencia.Agora)); break;
            default: throw new ArgumentOutOfRangeException(nameof(referencia));
        }

        var erro = await Assert.ThrowsAsync<DbUpdateException>(() => contexto.SaveChangesAsync(Cancelamento));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(erro.InnerException).SqlState);
    }

    [Fact]
    public async Task AuditoriaPersisteAsQuatroAcoesEPreservaHistoricoContraExclusaoFisica()
    {
        var usuario = await DadosPersistencia.GravarUsuarioAsync(banco);
        var agora = DadosPersistencia.Agora;
        var logs = new[]
        {
            LogUsuario.RegistrarCadastro(usuario.Id, agora),
            LogUsuario.RegistrarAlteracaoDados(usuario.Id, agora.AddMinutes(1)),
            LogUsuario.RegistrarTrocaPerfil(usuario.Id, agora.AddMinutes(2)),
            LogUsuario.RegistrarInativacao(usuario.Id, agora.AddMinutes(3))
        };
        await using (var escrita = banco.CriarContexto())
        {
            escrita.LogsUsuarios.AddRange(logs);
            await escrita.SaveChangesAsync(Cancelamento);
        }
        await using var leitura = banco.CriarContexto();
        var recuperados = await leitura.LogsUsuarios.AsNoTracking().Where(log => log.UsuarioId == usuario.Id)
            .OrderBy(log => log.DataCriacao).ToListAsync(Cancelamento);
        Assert.Equal(logs.Select(log => log.Descricao), recuperados.Select(log => log.Descricao));
        Assert.Equal(logs.Select(log => log.DataCriacao), recuperados.Select(log => log.DataCriacao));
        var erro = await Assert.ThrowsAsync<PostgresException>(() => leitura.Usuarios.Where(item => item.Id == usuario.Id)
            .ExecuteDeleteAsync(Cancelamento));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, erro.SqlState);
        Assert.Equal(4, await leitura.LogsUsuarios.CountAsync(log => log.UsuarioId == usuario.Id, Cancelamento));
    }
}
