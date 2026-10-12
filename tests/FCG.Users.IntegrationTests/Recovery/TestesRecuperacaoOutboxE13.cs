using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FCG.Users.Infrastructure.Data.Outbox;
using FCG.Users.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace FCG.Users.IntegrationTests.Recovery;

[CollectionDefinition(Nome, DisableParallelization = true)]
public sealed class ColecaoRecuperacaoE13 : ICollectionFixture<RabbitMqRecuperacaoFixture>
{
    public const string Nome = "E13 processos Kestrel e recuperacao real";
}

[Collection(ColecaoRecuperacaoE13.Nome)]
public sealed class TestesRecuperacaoOutboxE13(RabbitMqRecuperacaoFixture rabbit, ITestOutputHelper saida)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task BrokerParadoAceitaCadastroERetomaSemReiniciarApiPreservandoFilaDuravel()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        await using var cenario = await rabbit.NovoCenarioAsync();
        using var chaves = new ChavesJwtTeste();
        await using var api = await IniciarAsync(banco, chaves, cenario);
        var aquecimento = await CadastrarAsync(api);
        var anterior = await EsperarPublicacaoAsync(banco, aquecimento);
        var idContainer = rabbit.IdContainer;
        var porta = rabbit.Porta;
        await rabbit.PararAsync(Cancelamento);
        MensagemOutbox pendente;
        Guid usuario;
        try
        {
            using var saude = await api.Cliente.GetAsync("/health", Cancelamento);
            Assert.Equal(HttpStatusCode.OK, saude.StatusCode);
            usuario = await CadastrarAsync(api);
            await EsperarAsync(async () => (await LerAsync(banco, usuario)).Attempts > 0);
            pendente = await LerAsync(banco, usuario);
            Assert.Null(pendente.PublishedAt);
            await ExigirCadastroAtomicoAsync(banco, usuario);
        }
        finally { await rabbit.RetomarAsync(CancellationToken.None); }
        Assert.Equal(idContainer, rabbit.IdContainer);
        Assert.Equal(porta, rabbit.Porta);
        var publicado = await EsperarPublicacaoAsync(banco, usuario);
        Preservado(pendente, publicado);
        Assert.True(publicado.Attempts > pendente.Attempts);
        Assert.False(api.Encerrado);
        var mensagens = (await cenario.MensagensAsync()).EnumerateArray().ToArray();
        Assert.Equal(2, mensagens.Length);
        ExigirMensagemUnica(mensagens, anterior);
        ExigirMensagemUnica(mensagens, publicado);
        Evidencia("broker-retorna", new
        {
            processoApi = api.Id, mesmoProcessoApi = true, mesmoContainerBroker = true,
            eventosNaFila = mensagens.Length, pendente = Estado(pendente), publicado = Estado(publicado),
            eventoAnteriorAoRestartPreservado = anterior.EventId
        });
    }

    [Fact]
    public async Task ReinicioEntregaPendenciaENovoReinicioNaoReenviaEventoJaConcluido()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        await using var cenario = await rabbit.NovoCenarioAsync();
        await cenario.DesvincularAsync();
        using var chaves = new ChavesJwtTeste();
        await using var primeiro = await IniciarAsync(banco, chaves, cenario);
        var usuario = await CadastrarAsync(primeiro);
        await EsperarAsync(async () => (await LerAsync(banco, usuario)).Attempts > 0);
        var pendente = await LerAsync(banco, usuario);
        Assert.Null(pendente.PublishedAt);
        Assert.Empty((await cenario.MensagensAsync()).EnumerateArray());
        await primeiro.DerrubarAsync();
        var depoisDaQueda = await LerAsync(banco, usuario);
        Preservado(pendente, depoisDaQueda);
        Assert.Null(depoisDaQueda.PublishedAt);
        await cenario.VincularAsync();
        await using var segundo = await IniciarAsync(banco, chaves, cenario);
        var publicado = await EsperarPublicacaoAsync(banco, usuario);
        Preservado(pendente, publicado);
        Assert.NotEqual(primeiro.Id, segundo.Id);
        await segundo.DerrubarAsync();
        await using var terceiro = await IniciarAsync(banco, chaves, cenario);
        // Um novo evento publicado demonstra um ciclo produtivo após o segundo reinício.
        var sonda = await CadastrarAsync(terceiro);
        var sondaPublicada = await EsperarPublicacaoAsync(banco, sonda);
        var mensagens = (await cenario.MensagensAsync()).EnumerateArray().ToArray();
        Assert.Equal(2, mensagens.Length);
        ExigirMensagemUnica(mensagens, publicado);
        ExigirMensagemUnica(mensagens, sondaPublicada);
        var concluido = await LerAsync(banco, usuario);
        Assert.Equal(publicado.Attempts, concluido.Attempts);
        Assert.Equal(publicado.PublishedAt, concluido.PublishedAt);
        Evidencia("reinicio-com-pendencia", new
        {
            processos = new[] { primeiro.Id, segundo.Id, terceiro.Id }, pendente = Estado(pendente),
            publicado = Estado(publicado), eventoConcluidoReenviado = false, eventosNaFila = mensagens.Length
        });
    }

    [Fact]
    public async Task CrashDepoisDoEnvioAntesDaBaixaRepublicaMesmoEventoComBrokerReal()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        await using var cenario = await rabbit.NovoCenarioAsync();
        using var chaves = new ChavesJwtTeste();
        await using var preparacao = await IniciarAsync(banco, chaves, cenario, habilitado: false);
        var usuario = await CadastrarAsync(preparacao);
        var original = await LerAsync(banco, usuario);
        await preparacao.DerrubarAsync();
        await using var barreira = await BarreiraPublicacaoE13.CriarAsync(banco.Conexao, original.EventId, Cancelamento);
        await using var primeiro = await IniciarAsync(banco, chaves, cenario);
        await EsperarAsync(() => barreira.HostEsperandoAsync(primeiro.NomeAplicacaoBanco, Cancelamento));
        var emVoo = await LerAsync(banco, usuario);
        Assert.Null(emVoo.PublishedAt);
        Assert.Equal(0, emVoo.Attempts);
        ExigirMensagemUnica((await cenario.MensagensAsync()).EnumerateArray().ToArray(), original);
        // A publicação real terminou: o UPDATE de PublishedAt está esperando dentro do trigger.
        await primeiro.DerrubarAsync();
        await barreira.LiberarAsync(Cancelamento);
        await EsperarAsync(() => barreira.HostDesconectadoAsync(primeiro.NomeAplicacaoBanco, Cancelamento));
        await barreira.RemoverAsync(Cancelamento);
        var aposCrash = await LerAsync(banco, usuario);
        Preservado(original, aposCrash);
        Assert.Null(aposCrash.PublishedAt);
        Assert.Equal(0, aposCrash.Attempts);
        await using var segundo = await IniciarAsync(banco, chaves, cenario);
        var publicado = await EsperarPublicacaoAsync(banco, usuario);
        Preservado(original, publicado);
        var mensagens = (await cenario.MensagensAsync()).EnumerateArray().ToArray();
        Assert.Equal(2, mensagens.Length);
        foreach (var mensagem in mensagens) ExigirEnvelope(mensagem, original);
        Assert.Equal(1, publicado.Attempts); // A tentativa anterior não confirmou seu contador.
        Evidencia("crash-apos-envio", new
        {
            processosPublicadores = new[] { primeiro.Id, segundo.Id }, bancoAntesDaQueda = Estado(emVoo),
            bancoAposQueda = Estado(aposCrash), publicado = Estado(publicado), copiasMesmoEventoNaFila = 2,
            idsEConteudoPreservados = true, barreiraDeBancoObservada = true
        });
    }

    [Fact]
    public async Task DuasApisCoordenamLinhaReservadaEOutraInstanciaEntregaProximaMensagem()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        await using var cenario = await rabbit.NovoCenarioAsync();
        using var chaves = new ChavesJwtTeste();
        await using var preparacao = await IniciarAsync(banco, chaves, cenario, habilitado: false);
        var usuarioA = await CadastrarAsync(preparacao);
        var usuarioB = await CadastrarAsync(preparacao);
        var originalA = await LerAsync(banco, usuarioA);
        var originalB = await LerAsync(banco, usuarioB);
        await preparacao.DerrubarAsync();
        await using var barreira = await BarreiraPublicacaoE13.CriarAsync(banco.Conexao, originalA.EventId, Cancelamento);
        await using var apiA = await IniciarAsync(banco, chaves, cenario);
        await EsperarAsync(() => barreira.HostEsperandoAsync(apiA.NomeAplicacaoBanco, Cancelamento));
        await using var apiB = await IniciarAsync(banco, chaves, cenario);
        var publicadoB = await EsperarPublicacaoAsync(banco, usuarioB);
        Assert.False(apiA.Encerrado);
        Assert.False(apiB.Encerrado);
        Assert.NotEqual(apiA.Id, apiB.Id);
        var reservadoA = await LerAsync(banco, usuarioA);
        Assert.Null(reservadoA.PublishedAt);
        Assert.Equal(0, reservadoA.Attempts);
        var durante = (await cenario.MensagensAsync()).EnumerateArray().ToArray();
        Assert.Equal(2, durante.Length);
        ExigirMensagemUnica(durante, originalA);
        ExigirMensagemUnica(durante, originalB);
        Assert.Equal(1, publicadoB.Attempts);
        await barreira.LiberarAsync(Cancelamento);
        var publicadoA = await EsperarPublicacaoAsync(banco, usuarioA);
        await barreira.RemoverAsync(Cancelamento);
        Assert.Equal(1, publicadoA.Attempts);
        // Nova mensagem prova que os hosts seguem capazes de avançar após a liberação.
        var usuarioC = await CadastrarAsync(apiB);
        var publicadoC = await EsperarPublicacaoAsync(banco, usuarioC);
        var mensagens = (await cenario.MensagensAsync()).EnumerateArray().ToArray();
        Assert.Equal(3, mensagens.Length);
        ExigirMensagemUnica(mensagens, originalA);
        ExigirMensagemUnica(mensagens, originalB);
        ExigirMensagemUnica(mensagens, publicadoC);
        Evidencia("duas-instancias", new
        {
            processosSimultaneos = new[] { apiA.Id, apiB.Id }, reservado = Estado(reservadoA),
            entreguePelaOutraInstancia = Estado(publicadoB), reservadoConcluido = Estado(publicadoA),
            eventosNaFila = mensagens.Length, umaCopiaPorEventoNesteCenario = true
        });
    }

    [Fact]
    public async Task CadastroRevertidoNaoGeraMensagemMesmoComWorkerEBrokerAtivos()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        await using var cenario = await rabbit.NovoCenarioAsync();
        using var chaves = new ChavesJwtTeste();
        await using var api = await IniciarAsync(banco, chaves, cenario);
        await using var contexto = banco.CriarContexto();
        await contexto.Database.ExecuteSqlRawAsync("ALTER TABLE \"tb_LogUsuarios\" ADD CONSTRAINT \"CK_E13_FalhaCadastro\" CHECK (false)", Cancelamento);
        try
        {
            using var resposta = await api.Cliente.PostAsJsonAsync("/api/v1/usuarios", Cadastro(), Cancelamento);
            Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
            Assert.False(await contexto.Usuarios.AnyAsync(Cancelamento));
            Assert.False(await contexto.LogsUsuarios.AnyAsync(Cancelamento));
            Assert.False(await contexto.MensagensOutbox.AnyAsync(Cancelamento));
            Assert.Empty((await cenario.MensagensAsync()).EnumerateArray());
        }
        finally
        {
            await contexto.Database.ExecuteSqlRawAsync("ALTER TABLE \"tb_LogUsuarios\" DROP CONSTRAINT IF EXISTS \"CK_E13_FalhaCadastro\"", CancellationToken.None);
        }
        var valido = await CadastrarAsync(api);
        var publicado = await EsperarPublicacaoAsync(banco, valido);
        var mensagens = (await cenario.MensagensAsync()).EnumerateArray().ToArray();
        Assert.Single(mensagens);
        ExigirMensagemUnica(mensagens, publicado);
        Evidencia("cadastro-revertido", new
        {
            statusCadastroRevertido = 500, registrosDoCadastroRevertido = 0, mensagensDoCadastroRevertido = 0,
            recuperacaoDoFluxo = Estado(publicado), eventosNaFila = mensagens.Length
        });
    }

    private static Task<ProcessoUsersApiE13> IniciarAsync(BancoUsersFixture banco, ChavesJwtTeste chaves,
        RabbitMqFixture.CenarioRabbit cenario, bool habilitado = true)
    {
        var configuracao = cenario.Configuracao;
        configuracao["Outbox:Enabled"] = habilitado ? "true" : "false";
        return ProcessoUsersApiE13.IniciarAsync(banco.Conexao, chaves, configuracao, Cancelamento);
    }

    private static object Cadastro() => new
    {
        nome = "Pessoa Sintética E13", cpf = DadosPersistencia.ProximoCpf(),
        email = $"e13-{Guid.NewGuid():N}@exemplo.test", dataNascimento = "2000-01-01", senha = "SenhaSintetica#E13"
    };

    private static async Task<Guid> CadastrarAsync(ProcessoUsersApiE13 api)
    {
        using var resposta = await api.Cliente.PostAsJsonAsync("/api/v1/usuarios", Cadastro(), Cancelamento);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento)).GetProperty("id").GetGuid();
    }

    private static async Task<MensagemOutbox> LerAsync(BancoUsersFixture banco, Guid usuario)
    {
        await using var contexto = banco.CriarContexto();
        return await contexto.MensagensOutbox.AsNoTracking().SingleAsync(item => item.UserId == usuario, Cancelamento);
    }

    private static async Task<MensagemOutbox> EsperarPublicacaoAsync(BancoUsersFixture banco, Guid usuario)
    {
        await EsperarAsync(async () => (await LerAsync(banco, usuario)).PublishedAt.HasValue);
        return await LerAsync(banco, usuario);
    }

    private static async Task EsperarAsync(Func<Task<bool>> condicao)
    {
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(Cancelamento);
        prazo.CancelAfter(TimeSpan.FromSeconds(45));
        while (!await condicao()) await Task.Delay(75, prazo.Token);
    }

    private static async Task ExigirCadastroAtomicoAsync(BancoUsersFixture banco, Guid usuario)
    {
        await using var contexto = banco.CriarContexto();
        Assert.True(await contexto.Usuarios.AnyAsync(item => item.Id == usuario, Cancelamento));
        Assert.Equal(1, await contexto.LogsUsuarios.CountAsync(item => item.UsuarioId == usuario, Cancelamento));
        Assert.Equal(1, await contexto.MensagensOutbox.CountAsync(item => item.UserId == usuario, Cancelamento));
    }

    private static void Preservado(MensagemOutbox original, MensagemOutbox atual)
    {
        Assert.Equal(original.Id, atual.Id);
        Assert.Equal(original.EventId, atual.EventId);
        Assert.Equal(original.CorrelationId, atual.CorrelationId);
        Assert.Equal(original.UserId, atual.UserId);
        Assert.Equal(original.EventType, atual.EventType);
        Assert.Equal(original.OccurredAt, atual.OccurredAt);
        Assert.Equal(original.Payload, atual.Payload);
    }

    private static void ExigirMensagemUnica(JsonElement[] mensagens, MensagemOutbox evento)
    {
        var correspondentes = mensagens.Where(mensagem => EventId(mensagem) == evento.EventId).ToArray();
        ExigirEnvelope(Assert.Single(correspondentes), evento);
    }

    private static Guid EventId(JsonElement mensagem)
    {
        using var envelope = Envelope(mensagem);
        return envelope.RootElement.GetProperty("messageId").GetGuid();
    }

    private static JsonDocument Envelope(JsonElement mensagem) =>
        JsonDocument.Parse(Convert.FromBase64String(mensagem.GetProperty("payload").GetString()!));

    private static void ExigirEnvelope(JsonElement mensagem, MensagemOutbox evento)
    {
        using var envelope = Envelope(mensagem);
        Assert.Equal(evento.EventId, envelope.RootElement.GetProperty("messageId").GetGuid());
        Assert.Equal(evento.CorrelationId, envelope.RootElement.GetProperty("correlationId").GetGuid());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(evento.Payload), JsonNode.Parse(envelope.RootElement.GetProperty("message").GetRawText())));
        Assert.Equal(2, mensagem.GetProperty("properties").GetProperty("delivery_mode").GetInt32());
    }

    private static object Estado(MensagemOutbox evento) => new
    {
        evento.Id, evento.EventId, evento.CorrelationId, evento.UserId, evento.OccurredAt, evento.PublishedAt, evento.Attempts,
        payloadSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evento.Payload)))
    };

    private void Evidencia(string cenario, object dados) =>
        saida.WriteLine("E13-EVIDENCIA:" + JsonSerializer.Serialize(new { cenario, rabbitMqVersao = rabbit.Versao, dados }, JsonOptions));
}
