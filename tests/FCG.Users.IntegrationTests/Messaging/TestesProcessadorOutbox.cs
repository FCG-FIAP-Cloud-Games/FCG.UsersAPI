using System.Text.Json;
using System.Text.Json.Nodes;
using FCG.Notifications.Application.Messaging.Contracts;
using FCG.Users.Application.Abstractions.Repositories;
using FCG.Users.Domain.Entities;
using FCG.Users.Infrastructure.Data;
using FCG.Users.Infrastructure.Messaging;
using FCG.Users.Infrastructure.Repositories;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace FCG.Users.IntegrationTests.Messaging;

public sealed class TestesProcessadorOutbox
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SucessoRegistraPublicacaoSemRecriarIdsOuPayloadENaoReprocessaConcluido()
    {
        await using var banco = await BancoAsync();
        var evento = await CadastrarAsync(banco);
        var original = await LerAsync(banco, evento.EventId);
        var transporte = new TransporteStub();
        await using var contexto = banco.CriarContexto();
        using var logs = new ColetorLogs();
        using var fabricaLogs = LoggerFactory.Create(configurar => configurar.AddProvider(logs));
        var processador = Criar(contexto, transporte, logger: fabricaLogs.CreateLogger<ProcessadorOutbox>());
        Assert.Equal(new ResultadoLoteOutbox(1, 1, 0, 0), await processador.ProcessarLoteAsync(Cancelamento));
        var atual = await LerAsync(banco, evento.EventId);
        Assert.Equal(1, atual.Attempts);
        Assert.Equal(DadosPersistencia.Agora.AddDays(1), atual.PublishedAt);
        Assert.Equal(original.Payload, atual.Payload);
        Assert.Equal(evento, Assert.Single(transporte.Eventos));
        Assert.Contains(original.EventType, logs.Texto, StringComparison.Ordinal);
        Assert.Contains(evento.EventId.ToString(), logs.Texto, StringComparison.Ordinal);
        Assert.Contains(evento.CorrelationId.ToString(), logs.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain(evento.Email, logs.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain(evento.Name, logs.Texto, StringComparison.Ordinal);
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Equal(new ResultadoLoteOutbox(0, 0, 0, 0), await processador.ProcessarLoteAsync(Cancelamento));
        Assert.Single(transporte.Eventos);
    }

    [Fact]
    public async Task FalhaDeTransporteMantemPendenciaEGravaTentativaSemSegredoNosLogs()
    {
        await using var banco = await BancoAsync();
        var evento = await CadastrarAsync(banco);
        var original = await LerAsync(banco, evento.EventId);
        using var logs = new ColetorLogs();
        using var fabricaLogs = LoggerFactory.Create(configurar => configurar.AddProvider(logs));
        var transporte = new TransporteStub((_, _) => Task.FromException(new IOException("falha contendo " + evento.Email)));
        await using var contexto = banco.CriarContexto();
        var processador = Criar(contexto, transporte, logger: fabricaLogs.CreateLogger<ProcessadorOutbox>());
        Assert.Equal(new ResultadoLoteOutbox(1, 0, 1, 1), await processador.ProcessarLoteAsync(Cancelamento));
        var atual = await LerAsync(banco, evento.EventId);
        Assert.Null(atual.PublishedAt);
        Assert.Equal(1, atual.Attempts);
        Assert.Equal(original.Payload, atual.Payload);
        Assert.Contains(evento.EventId.ToString(), logs.Texto, StringComparison.Ordinal);
        Assert.Contains("FALHA_TRANSPORTE", logs.Texto, StringComparison.Ordinal);
        Assert.Contains(original.EventType, logs.Texto, StringComparison.Ordinal);
        Assert.Contains(evento.CorrelationId.ToString(), logs.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain(evento.Email, logs.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain(evento.Name, logs.Texto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tipo")]
    [InlineData("metadata")]
    [InlineData("versao")]
    [InlineData("nome")]
    [InlineData("camposExtras")]
    [InlineData("objetoVazio")]
    public async Task RegistroInvalidoNaoEEnviadoENaoImpedeOutroEventoValido(string campo)
    {
        await using var banco = await BancoAsync();
        var invalido = await CadastrarAsync(banco);
        var valido = await CadastrarAsync(banco);
        await using var contexto = banco.CriarContexto();
        var mensagem = await LerAsync(banco, invalido.EventId);
        if (campo == "tipo")
            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"tb_Outbox\" SET \"EventType\" = {"urn:message:TipoDesconhecido"} WHERE \"EventId\" = {invalido.EventId}", Cancelamento);
        else if (campo == "metadata")
            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"tb_Outbox\" SET \"CorrelationId\" = {Guid.NewGuid()} WHERE \"EventId\" = {invalido.EventId}", Cancelamento);
        else
        {
            var json = JsonNode.Parse(mensagem.Payload)!;
            if (campo == "versao") json["version"] = 2;
            if (campo == "nome") json["name"] = "";
            if (campo == "camposExtras") json["cpf"] = "dado-que-nao-deve-sair";
            var payload = campo == "objetoVazio" ? "{}" : json.ToJsonString();
            await contexto.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"tb_Outbox\" SET \"Payload\" = {payload}::jsonb WHERE \"EventId\" = {invalido.EventId}", Cancelamento);
        }
        var transporte = new TransporteStub();
        var resultado = await Criar(contexto, transporte).ProcessarLoteAsync(Cancelamento);
        Assert.Equal(new ResultadoLoteOutbox(2, 1, 1, 1), resultado);
        Assert.Equal(valido, Assert.Single(transporte.Eventos));
        Assert.Null((await LerAsync(banco, invalido.EventId)).PublishedAt);
        Assert.NotNull((await LerAsync(banco, valido.EventId)).PublishedAt);
    }

    [Fact]
    public async Task LinhaReservadaPorOutraInstanciaEPuladaSemEsperarOLock()
    {
        await using var banco = await BancoAsync();
        var evento = await CadastrarAsync(banco);
        await using var dono = banco.CriarContexto();
        await using var transacao = await dono.Database.BeginTransactionAsync(Cancelamento);
        await dono.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"tb_Outbox\" WHERE \"EventId\" = {evento.EventId} FOR UPDATE", Cancelamento);
        var transporte = new TransporteStub();
        await using var contexto = banco.CriarContexto();
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(Cancelamento);
        prazo.CancelAfter(TimeSpan.FromSeconds(3));
        Assert.Equal(new ResultadoLoteOutbox(0, 0, 0, 0), await Criar(contexto, transporte).ProcessarLoteAsync(prazo.Token));
        Assert.Empty(transporte.Eventos);
        await transacao.RollbackAsync(Cancelamento);
        Assert.Equal(1, (await Criar(contexto, transporte).ProcessarLoteAsync(Cancelamento)).Publicadas);
    }

    [Fact]
    public async Task LimiteDoLoteEEventoProblematicoNaoImpedemProgressoDasDemaisPendencias()
    {
        await using var banco = await BancoAsync();
        await CadastrarAsync(banco);
        await CadastrarAsync(banco);
        await using var contexto = banco.CriarContexto();
        var primeiro = await contexto.MensagensOutbox.OrderBy(item => item.OccurredAt).ThenBy(item => item.Id)
            .Select(item => item.EventId).FirstAsync(Cancelamento);
        var transporte = new TransporteStub((evento, _) => evento.EventId == primeiro
            ? Task.FromException(new IOException("falha sintética")) : Task.CompletedTask);
        var processador = Criar(contexto, transporte, ("Outbox:BatchSize", "1"));
        Assert.Equal(new ResultadoLoteOutbox(1, 0, 1, 1), await processador.ProcessarLoteAsync(Cancelamento));
        Assert.Equal(new ResultadoLoteOutbox(1, 1, 0, 0), await processador.ProcessarLoteAsync(Cancelamento));
        Assert.Null((await LerAsync(banco, primeiro)).PublishedAt);
    }


    [Fact]
    public async Task TimeoutMantemEventoPendenteEContaATentativa()
    {
        await using var banco = await BancoAsync();
        var evento = await CadastrarAsync(banco);
        var transporte = new TransporteStub((_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));
        await using var contexto = banco.CriarContexto();
        var resultado = await Criar(contexto, transporte, ("Outbox:PublishTimeoutSeconds", "0.1")).ProcessarLoteAsync(Cancelamento);
        Assert.Equal(new ResultadoLoteOutbox(1, 0, 1, 1), resultado);
        var atual = await LerAsync(banco, evento.EventId);
        Assert.Null(atual.PublishedAt);
        Assert.Equal(1, atual.Attempts);
    }

    [Fact]
    public async Task CancelamentoDeEncerramentoDesfazBaixaESoltaReserva()
    {
        await using var banco = await BancoAsync();
        var evento = await CadastrarAsync(banco);
        var iniciou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transporte = new TransporteStub((_, token) => { iniciou.SetResult(); return Task.Delay(Timeout.InfiniteTimeSpan, token); });
        await using var contexto = banco.CriarContexto();
        using var parar = CancellationTokenSource.CreateLinkedTokenSource(Cancelamento);
        var tarefa = Criar(contexto, transporte).ProcessarLoteAsync(parar.Token);
        await iniciou.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancelamento);
        await parar.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarefa);
        var atual = await LerAsync(banco, evento.EventId);
        Assert.Null(atual.PublishedAt);
        Assert.Equal(0, atual.Attempts); // A tentativa não chegou a ser confirmada no banco.
        Assert.Empty(contexto.ChangeTracker.Entries());
        Assert.Equal(1, (await Criar(contexto, new TransporteStub()).ProcessarLoteAsync(Cancelamento)).Publicadas);
    }

    [Fact]
    public async Task FalhaNoCommitDaBaixaPermiteReenvioComMesmosIdsEPayload()
    {
        await using var banco = await BancoAsync();
        var evento = await CadastrarAsync(banco);
        await using var contexto = banco.CriarContexto();
        await contexto.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION e12_rejeitar_baixa() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW."PublishedAt" IS NOT NULL THEN
                    RAISE EXCEPTION 'Falha sintética da baixa E12.' USING ERRCODE = '23514', CONSTRAINT = 'CK_E12_Commit';
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE CONSTRAINT TRIGGER e12_commit AFTER UPDATE ON "tb_Outbox"
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION e12_rejeitar_baixa();
            """, Cancelamento);
        var transporte = new TransporteStub();
        var processador = Criar(contexto, transporte);
        try
        {
            var erro = await Assert.ThrowsAsync<PostgresException>(() => processador.ProcessarLoteAsync(Cancelamento));
            Assert.Equal("CK_E12_Commit", erro.ConstraintName);
            var atual = await LerAsync(banco, evento.EventId);
            Assert.Null(atual.PublishedAt);
            Assert.Equal(0, atual.Attempts);
            Assert.Equal(evento, Assert.Single(transporte.Eventos));
        }
        finally
        {
            await contexto.Database.ExecuteSqlRawAsync(
                "DROP TRIGGER IF EXISTS e12_commit ON \"tb_Outbox\"; DROP FUNCTION IF EXISTS e12_rejeitar_baixa()", CancellationToken.None);
        }
        Assert.Equal(1, (await processador.ProcessarLoteAsync(Cancelamento)).Publicadas);
        Assert.Equal(2, transporte.Eventos.Count);
        Assert.All(transporte.Eventos, enviado => Assert.Equal(evento, enviado));
    }

    [Fact]
    public async Task DuasInstanciasNaoPublicamALinhaEnquantoAReservaEstaRetida()
    {
        await using var banco = await BancoAsync();
        await CadastrarAsync(banco);
        var iniciou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transporte = new TransporteStub((_, token) => { iniciou.TrySetResult(); return liberar.Task.WaitAsync(token); });
        await using var primeiro = banco.CriarContexto();
        await using var segundo = banco.CriarContexto();
        var tarefa = Criar(primeiro, transporte).ProcessarLoteAsync(Cancelamento);
        try
        {
            await iniciou.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancelamento);
            Assert.Equal(0, (await Criar(segundo, transporte).ProcessarLoteAsync(Cancelamento)).Tentativas);
        }
        finally { liberar.TrySetResult(); }
        Assert.Equal(1, (await tarefa).Publicadas);
        Assert.Single(transporte.Eventos);
    }

    private static async Task<BancoUsersFixture> BancoAsync()
    {
        var banco = new BancoUsersFixture();
        try { await banco.InitializeAsync(); return banco; }
        catch { await banco.DisposeAsync(); throw; }
    }

    private static async Task<UserCreatedEvent> CadastrarAsync(BancoUsersFixture banco)
    {
        var usuario = DadosPersistencia.NovoUsuario();
        var evento = DadosPersistencia.EventoCriado(usuario);
        await using var contexto = banco.CriarContexto();
        Assert.Equal(ResultadoGravacaoUsuario.Sucesso, await new RepositorioUsuarios(contexto).TentarAdicionarAsync(
            usuario, LogUsuario.RegistrarCadastro(usuario.Id, usuario.CriadoEmUtc), evento, Cancelamento));
        return evento;
    }

    private static async Task<FCG.Users.Infrastructure.Data.Outbox.MensagemOutbox> LerAsync(BancoUsersFixture banco, Guid eventId)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.MensagensOutbox.AsNoTracking().SingleAsync(item => item.EventId == eventId, Cancelamento);
    }

    private static ProcessadorOutbox Criar(UsersDbContext contexto, TransporteStub transporte,
        params (string Chave, string Valor)[] valores) =>
        Criar(contexto, transporte, NullLogger<ProcessadorOutbox>.Instance, valores);

    private static ProcessadorOutbox Criar(UsersDbContext contexto, TransporteStub transporte,
        ILogger<ProcessadorOutbox> logger, params (string Chave, string Valor)[] valores)
    {
        var dados = new Dictionary<string, string?> { ["Outbox:Enabled"] = "true" };
        foreach (var (chave, valor) in valores) dados[chave] = valor;
        return new(contexto, transporte, ConfiguracaoPublicadorOutbox.Criar(
            new ConfigurationBuilder().AddInMemoryCollection(dados).Build()), new RelogioFixo(), logger);
    }

    private sealed class RelogioFixo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DadosPersistencia.Agora.AddDays(1);
    }

    private sealed class TransporteStub(Func<UserCreatedEvent, CancellationToken, Task>? acao = null) : IPublicadorEventos
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<UserCreatedEvent> _eventos = new();
        public IReadOnlyList<UserCreatedEvent> Eventos => _eventos.ToArray();
        public Task PublicarAsync(UserCreatedEvent evento, CancellationToken tokenCancelamento = default)
        {
            _eventos.Enqueue(evento);
            return acao?.Invoke(evento, tokenCancelamento) ?? Task.CompletedTask;
        }
    }
}
