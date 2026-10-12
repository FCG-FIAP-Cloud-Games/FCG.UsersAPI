using System.Text.Json;
using FCG.Notifications.Application.Messaging.Contracts;
using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Application.Messaging;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Data;
using FCG.Users.Infrastructure.Data.Outbox;
using FCG.Users.Infrastructure.Repositories;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace FCG.Users.IntegrationTests.Persistence;

[Collection(ColecaoBancoUsers.Nome)]
public sealed class TestesOutboxCadastro(BancoUsersFixture banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CadastroPersisteUsuarioAuditoriaEPayloadPendenteSemSegredos()
    {
        var usuario = DadosPersistencia.NovoUsuario();
        var evento = DadosPersistencia.EventoCriado(usuario);
        await using (var escrita = banco.CriarContexto())
            Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await CadastrarAsync(escrita, usuario, evento));
        await using var leitura = banco.CriarContexto();
        Assert.True(await leitura.Usuarios.AnyAsync(item => item.Id == usuario.Id, Cancelamento));
        Assert.Equal(1, await leitura.LogsUsuarios.CountAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
        var mensagem = await leitura.MensagensOutbox.SingleAsync(item => item.UserId == usuario.Id, Cancelamento);
        Assert.Equal(evento.EventId, mensagem.EventId);
        Assert.Equal(evento.CorrelationId, mensagem.CorrelationId);
        Assert.Equal(DefinicaoUserCreatedEvent.Tipo, mensagem.EventType);
        Assert.Equal(evento.OccurredAt, mensagem.OccurredAt);
        Assert.Null(mensagem.PublishedAt);
        Assert.Equal(0, mensagem.Attempts);
        using var payload = JsonDocument.Parse(mensagem.Payload);
        string[] campos = ["eventId", "correlationId", "occurredAt", "version", "userId", "name", "email"];
        Assert.Equal(campos.Order(), payload.RootElement.EnumerateObject().Select(item => item.Name).Order());
        Assert.Equal(usuario.Id, payload.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(usuario.Nome, payload.RootElement.GetProperty("name").GetString());
        Assert.Equal(usuario.Email, payload.RootElement.GetProperty("email").GetString());
        Assert.Equal(1, payload.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(TimeSpan.Zero, payload.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Offset);
        Assert.DoesNotContain(usuario.CPF, mensagem.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain(usuario.SenhaHash, mensagem.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FalhaNaOutboxDesfazAsTresInclusoesELimpaContextoParaNovoCadastro()
    {
        var usuario = DadosPersistencia.NovoUsuario();
        var evento = DadosPersistencia.EventoCriado(usuario);
        await using var contexto = banco.CriarContexto();
        var sql = $"ALTER TABLE \"tb_Outbox\" ADD CONSTRAINT \"CK_E11_RejeitarEvento\" CHECK (\"EventId\" <> '{evento.EventId:D}'::uuid)";
        await contexto.Database.ExecuteSqlRawAsync(sql, Cancelamento);
        try
        {
            var erro = await Assert.ThrowsAsync<DbUpdateException>(() => CadastrarAsync(contexto, usuario, evento));
            var postgres = Assert.IsType<PostgresException>(erro.InnerException);
            Assert.Equal(PostgresErrorCodes.CheckViolation, postgres.SqlState);
            Assert.Equal("CK_E11_RejeitarEvento", postgres.ConstraintName);
            foreach (var valor in new[] { usuario.Email, usuario.Nome, usuario.CPF, usuario.SenhaHash })
                Assert.DoesNotContain(valor, erro.ToString(), StringComparison.Ordinal);
            Assert.Empty(contexto.ChangeTracker.Entries());
            await ExigirAusenciaAsync(usuario.Id);
            var seguinte = DadosPersistencia.NovoUsuario();
            Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await CadastrarAsync(contexto, seguinte, DadosPersistencia.EventoCriado(seguinte)));
            await using var leitura = banco.CriarContexto();
            Assert.Equal(1, await leitura.MensagensOutbox.CountAsync(item => item.UserId == seguinte.Id, Cancelamento));
        }
        finally { await contexto.Database.ExecuteSqlRawAsync("ALTER TABLE \"tb_Outbox\" DROP CONSTRAINT IF EXISTS \"CK_E11_RejeitarEvento\"", CancellationToken.None); }
    }

    [Fact]
    public async Task EventIdRepetidoNaoCriaSegundoUsuarioNemDescartaOEventoOriginal()
    {
        var primeiro = DadosPersistencia.NovoUsuario();
        var original = DadosPersistencia.EventoCriado(primeiro);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await CadastrarAsync(contexto, primeiro, original));
        contexto.ChangeTracker.Clear();
        var segundo = DadosPersistencia.NovoUsuario();
        var repetido = DadosPersistencia.EventoCriado(segundo, eventId: original.EventId);
        var erro = await Assert.ThrowsAsync<DbUpdateException>(() => CadastrarAsync(contexto, segundo, repetido));
        Assert.Equal("IX_tb_Outbox_EventId", Assert.IsType<PostgresException>(erro.InnerException).ConstraintName);
        Assert.Empty(contexto.ChangeTracker.Entries());
        await ExigirAusenciaAsync(segundo.Id);
        await using var leitura = banco.CriarContexto();
        var preservado = await leitura.MensagensOutbox.SingleAsync(item => item.EventId == original.EventId, Cancelamento);
        Assert.Equal(primeiro.Id, preservado.UserId);
        Assert.Equal(original.CorrelationId, preservado.CorrelationId);
    }

    [Theory]
    [InlineData("usuario")]
    [InlineData("nome")]
    [InlineData("email")]
    [InlineData("instante")]
    [InlineData("eventId")]
    [InlineData("correlationId")]
    [InlineData("versao")]
    [InlineData("utc")]
    public async Task EventoIncoerenteERecusadoAntesDeRastrearOuSalvar(string campo)
    {
        var usuario = DadosPersistencia.NovoUsuario();
        var evento = DadosPersistencia.EventoCriado(usuario);
        evento = campo switch
        {
            "usuario" => evento with { UserId = Guid.NewGuid() },
            "nome" => evento with { Name = "Outro nome" },
            "email" => evento with { Email = "outro@exemplo.test" },
            "instante" => evento with { OccurredAt = evento.OccurredAt.AddSeconds(1) },
            "eventId" => evento with { EventId = Guid.Empty },
            "correlationId" => evento with { CorrelationId = Guid.Empty },
            "versao" => evento with { Version = 2 },
            "utc" => evento with { OccurredAt = evento.OccurredAt.ToOffset(TimeSpan.FromHours(-3)) },
            _ => throw new ArgumentOutOfRangeException(nameof(campo))
        };
        await using var contexto = banco.CriarContexto();
        await Assert.ThrowsAsync<ArgumentException>(() => CadastrarAsync(contexto, usuario, evento));
        Assert.Empty(contexto.ChangeTracker.Entries());
        await ExigirAusenciaAsync(usuario.Id);
    }

    [Fact]
    public async Task TransacaoExternaNaoExpoeCadastrosAntesDoCommitERollbackDesfazTudo()
    {
        var usuario = DadosPersistencia.NovoUsuario();
        var evento = DadosPersistencia.EventoCriado(usuario);
        await using var contexto = banco.CriarContexto();
        await using var transacao = await contexto.Database.BeginTransactionAsync(Cancelamento);
        Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await CadastrarAsync(contexto, usuario, evento));
        Assert.Equal(1, await contexto.MensagensOutbox.CountAsync(item => item.UserId == usuario.Id, Cancelamento));
        await ExigirAusenciaAsync(usuario.Id);
        await transacao.RollbackAsync(Cancelamento);
        await ExigirAusenciaAsync(usuario.Id);
    }

    [Fact]
    public async Task FalhaAdiadaAteCommitNaoDeixaUsuarioAuditoriaOuEventoPersistidos()
    {
        var usuario = DadosPersistencia.NovoUsuario();
        var evento = DadosPersistencia.EventoCriado(usuario);
        await using var contexto = banco.CriarContexto();
        await contexto.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION e11_rejeitar_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW."EventId" = TG_ARGV[0]::uuid THEN
                    RAISE EXCEPTION 'Falha sintética no commit E11.' USING ERRCODE = '23514', CONSTRAINT = 'CK_E11_Commit';
                END IF;
                RETURN NEW;
            END;
            $$;
            """, Cancelamento);
        try
        {
            // DDL não recebe esse UUID por parâmetro. Origem: Guid gerado pelo próprio teste, nunca entrada externa.
            var criarGatilho = "CREATE CONSTRAINT TRIGGER e11_falha_commit AFTER INSERT ON \"tb_Outbox\" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION e11_rejeitar_commit('"
                + evento.EventId.ToString("D") + "')";
            await contexto.Database.ExecuteSqlRawAsync(criarGatilho, Cancelamento);
            await using (var transacao = await contexto.Database.BeginTransactionAsync(Cancelamento))
            {
                Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await CadastrarAsync(contexto, usuario, evento));
                Assert.True(await contexto.Usuarios.AnyAsync(item => item.Id == usuario.Id, Cancelamento));
                Assert.True(await contexto.LogsUsuarios.AnyAsync(item => item.UsuarioId == usuario.Id, Cancelamento));
                Assert.True(await contexto.MensagensOutbox.AnyAsync(item => item.UserId == usuario.Id, Cancelamento));
                var erro = await Assert.ThrowsAsync<PostgresException>(() => transacao.CommitAsync(Cancelamento));
                Assert.Equal("CK_E11_Commit", erro.ConstraintName);
            }
            await ExigirAusenciaAsync(usuario.Id);
        }
        finally { await contexto.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS e11_falha_commit ON \"tb_Outbox\"; DROP FUNCTION IF EXISTS e11_rejeitar_commit()", CancellationToken.None); }
    }

    [Fact]
    public async Task EdicaoPerfilEInativacaoNaoAlteramSnapshotNemGeramNovosUserCreatedEvents()
    {
        var usuario = DadosPersistencia.NovoUsuario();
        await using var contexto = banco.CriarContexto();
        await CadastrarAsync(contexto, usuario, DadosPersistencia.EventoCriado(usuario));
        var snapshot = await contexto.MensagensOutbox.AsNoTracking().SingleAsync(item => item.UserId == usuario.Id, Cancelamento);
        var repositorio = new RepositorioUsuarios(contexto);
        usuario.AtualizarDados("Nome depois do cadastro", usuario.DataNascimento, $"novo-{Guid.NewGuid():N}@exemplo.test");
        await repositorio.AtualizarAsync(usuario, LogUsuario.RegistrarAlteracaoDados(usuario.Id, usuario.CriadoEmUtc.AddSeconds(1)), Cancelamento);
        usuario.AlterarPerfil(PerfisSistema.AdministradorId);
        await repositorio.AtualizarAsync(usuario, LogUsuario.RegistrarTrocaPerfil(usuario.Id, usuario.CriadoEmUtc.AddSeconds(2)), Cancelamento);
        usuario.Inativar(usuario.CriadoEmUtc.AddSeconds(3));
        await repositorio.AtualizarAsync(usuario, LogUsuario.RegistrarInativacao(usuario.Id, usuario.CriadoEmUtc.AddSeconds(3)), Cancelamento);
        await using var leitura = banco.CriarContexto();
        var preservado = await leitura.MensagensOutbox.SingleAsync(item => item.UserId == usuario.Id, Cancelamento);
        Assert.Equal(snapshot.Payload, preservado.Payload);
        Assert.Equal(snapshot.EventId, preservado.EventId);
        Assert.Equal(snapshot.CorrelationId, preservado.CorrelationId);
        Assert.Equal(snapshot.OccurredAt, preservado.OccurredAt);
        Assert.Null(preservado.PublishedAt);
        Assert.Equal(0, preservado.Attempts);
    }

    [Theory]
    [InlineData("attempts", "CK_tb_Outbox_Attempts")]
    [InlineData("correlationId", "CK_tb_Outbox_Ids")]
    [InlineData("payload", "CK_tb_Outbox_PayloadObjeto")]
    public async Task BancoProtegeMetadadosMesmoContraAlteracaoDireta(string campo, string constraint)
    {
        var usuario = DadosPersistencia.NovoUsuario();
        await using var contexto = banco.CriarContexto();
        await CadastrarAsync(contexto, usuario, DadosPersistencia.EventoCriado(usuario));
        var sql = campo switch
        {
            "attempts" => $"UPDATE \"tb_Outbox\" SET \"Attempts\" = -1 WHERE \"UserId\" = '{usuario.Id:D}'::uuid",
            "correlationId" => $"UPDATE \"tb_Outbox\" SET \"CorrelationId\" = '00000000-0000-0000-0000-000000000000'::uuid WHERE \"UserId\" = '{usuario.Id:D}'::uuid",
            "payload" => $"UPDATE \"tb_Outbox\" SET \"Payload\" = '[]'::jsonb WHERE \"UserId\" = '{usuario.Id:D}'::uuid",
            _ => throw new ArgumentOutOfRangeException(nameof(campo))
        };
        var erro = await Assert.ThrowsAsync<PostgresException>(() => contexto.Database.ExecuteSqlRawAsync(sql, Cancelamento));
        Assert.Equal(constraint, erro.ConstraintName);
    }

    [Fact]
    public async Task CancelamentoDuranteSaveLimpaAsInclusoesENaoContaminaNovoCadastro()
    {
        using var cancelamento = CancellationTokenSource.CreateLinkedTokenSource(Cancelamento);
        var falha = new CancelarUmaGravacao(cancelamento);
        var opcoes = new DbContextOptionsBuilder<UsersDbContext>().UseNpgsql(banco.Conexao).AddInterceptors(falha).Options;
        await using var contexto = new UsersDbContext(opcoes);
        var usuario = DadosPersistencia.NovoUsuario();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RepositorioUsuarios(contexto).TentarAdicionarAsync(
            usuario, LogUsuario.RegistrarCadastro(usuario.Id, usuario.CriadoEmUtc), DadosPersistencia.EventoCriado(usuario), cancelamento.Token));
        Assert.Equal(3, falha.InclusoesObservadas);
        Assert.Empty(contexto.ChangeTracker.Entries());
        await ExigirAusenciaAsync(usuario.Id);
        var seguinte = DadosPersistencia.NovoUsuario();
        Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await CadastrarAsync(contexto, seguinte, DadosPersistencia.EventoCriado(seguinte)));
    }

    private static Task<ResultadoGravacaoUsuario> CadastrarAsync(UsersDbContext contexto, Usuario usuario, UserCreatedEvent evento) =>
        new RepositorioUsuarios(contexto).TentarAdicionarAsync(usuario, LogUsuario.RegistrarCadastro(usuario.Id, usuario.CriadoEmUtc), evento, Cancelamento);

    private async Task ExigirAusenciaAsync(Guid id)
    {
        await using var verificacao = banco.CriarContexto();
        Assert.False(await verificacao.Usuarios.AnyAsync(item => item.Id == id, Cancelamento));
        Assert.False(await verificacao.LogsUsuarios.AnyAsync(item => item.UsuarioId == id, Cancelamento));
        Assert.False(await verificacao.MensagensOutbox.AnyAsync(item => item.UserId == id, Cancelamento));
    }

    private sealed class CancelarUmaGravacao(CancellationTokenSource cancelamento) : SaveChangesInterceptor
    {
        public int InclusoesObservadas { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!cancelamento.IsCancellationRequested)
            {
                InclusoesObservadas = eventData.Context!.ChangeTracker.Entries().Count(item => item.State == EntityState.Added);
                cancelamento.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return ValueTask.FromResult(result);
        }
    }
}
