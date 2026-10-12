using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FCG.Users.Infrastructure.Messaging;
using FCG.Users.IntegrationTests.Support;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FCG.Users.IntegrationTests.Messaging;

[Collection(ColecaoMensageria.Nome)]
public sealed class TestesPublicadorOutboxHost(RabbitMqFixture rabbit)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HostHabilitadoPublicaCadastroERegistraConclusaoAutomaticamente()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        await using var cenario = await rabbit.NovoCenarioAsync();
        await using var fabrica = new FabricaUsersApi(conexao: banco.Conexao, sobrescreverConfiguracao: cenario.Configuracao);
        using var cliente = fabrica.CreateClient();
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(Cancelamento);
        prazo.CancelAfter(TimeSpan.FromSeconds(15));
        var correlacao = Guid.NewGuid();
        cliente.DefaultRequestHeaders.Add("X-Correlation-ID", correlacao.ToString("D"));
        using var resposta = await cliente.PostAsJsonAsync("/api/v1/usuarios", Cadastro(), prazo.Token);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var id = (await resposta.Content.ReadFromJsonAsync<JsonElement>(prazo.Token)).GetProperty("id").GetGuid();
        await EsperarAsync(async () =>
        {
            await using var contexto = banco.CriarContexto();
            return await contexto.MensagensOutbox.AnyAsync(item => item.UserId == id && item.PublishedAt != null, prazo.Token);
        }, prazo.Token);
        await using var leitura = banco.CriarContexto();
        var outbox = await leitura.MensagensOutbox.SingleAsync(item => item.UserId == id, prazo.Token);
        Assert.Equal(1, outbox.Attempts);
        Assert.Equal(correlacao, outbox.CorrelationId);
        var mensagem = Assert.Single((await cenario.MensagemAsync()).EnumerateArray());
        using var envelope = JsonDocument.Parse(Convert.FromBase64String(mensagem.GetProperty("payload").GetString()!));
        Assert.Equal(outbox.EventId, envelope.RootElement.GetProperty("messageId").GetGuid());
        Assert.Equal(outbox.CorrelationId, envelope.RootElement.GetProperty("correlationId").GetGuid());
        Assert.Equal(outbox.UserId, envelope.RootElement.GetProperty("message").GetProperty("userId").GetGuid());
        using var payload = JsonDocument.Parse(outbox.Payload);
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(payload.RootElement.GetRawText()),
            System.Text.Json.Nodes.JsonNode.Parse(envelope.RootElement.GetProperty("message").GetRawText())));
    }

    [Fact]
    public async Task SemRotaMantemPendenciaECorrigirBindingPermitePublicarMesmosDados()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        await using var cenario = await rabbit.NovoCenarioAsync();
        await cenario.DesvincularAsync();
        using var logs = new ColetorLogs();
        await using var fabrica = new FabricaUsersApi(conexao: banco.Conexao,
            configurarServicos: servicos => servicos.AddLogging(logging => logging.AddProvider(logs)),
            sobrescreverConfiguracao: cenario.Configuracao);
        using var cliente = fabrica.CreateClient();
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(Cancelamento);
        prazo.CancelAfter(TimeSpan.FromSeconds(20));
        using var resposta = await cliente.PostAsJsonAsync("/api/v1/usuarios", Cadastro(), prazo.Token);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var id = (await resposta.Content.ReadFromJsonAsync<JsonElement>(prazo.Token)).GetProperty("id").GetGuid();
        await EsperarAsync(async () =>
        {
            await using var contexto = banco.CriarContexto();
            return await contexto.MensagensOutbox.AnyAsync(item => item.UserId == id && item.Attempts > 0, prazo.Token);
        }, prazo.Token);
        await using var leitura = banco.CriarContexto();
        var antes = await leitura.MensagensOutbox.AsNoTracking().SingleAsync(item => item.UserId == id, prazo.Token);
        Assert.Null(antes.PublishedAt);
        Assert.Empty((await cenario.MensagemAsync()).EnumerateArray());
        await cenario.VincularAsync();
        await EsperarAsync(async () =>
        {
            await using var contexto = banco.CriarContexto();
            return await contexto.MensagensOutbox.AnyAsync(item => item.UserId == id && item.PublishedAt != null, prazo.Token);
        }, prazo.Token);
        var depois = await leitura.MensagensOutbox.AsNoTracking().SingleAsync(item => item.UserId == id, prazo.Token);
        Assert.Equal(antes.EventId, depois.EventId);
        Assert.Equal(antes.CorrelationId, depois.CorrelationId);
        Assert.Equal(antes.Payload, depois.Payload);
        Assert.True(depois.Attempts >= 2);
        Assert.Contains("SEM_ROTA", logs.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain(cenario.Password, logs.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain("SenhaSintetica#E12", logs.Texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BrokerInacessivelNaoImpedeStartupOuCadastroComPublicadorHabilitado()
    {
        await using var banco = new BancoUsersFixture();
        await banco.InitializeAsync();
        var configuracao = new Dictionary<string, string?>
        {
            ["Outbox:Enabled"] = "true", ["Outbox:PollIntervalSeconds"] = "0.1",
            ["Outbox:PublishTimeoutSeconds"] = "0.2", ["Outbox:InitialRetrySeconds"] = "0.1", ["Outbox:MaxRetrySeconds"] = "0.2",
            ["RabbitMq:Host"] = "127.0.0.1", ["RabbitMq:Port"] = "1",
            ["RabbitMq:VirtualHost"] = "e12-sem-broker", ["RabbitMq:Username"] = "e12_produtor", ["RabbitMq:Password"] = "senha-sintetica-sem-broker"
        };
        await using var fabrica = new FabricaUsersApi(conexao: banco.Conexao, sobrescreverConfiguracao: configuracao);
        using var cliente = fabrica.CreateClient();
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(Cancelamento);
        prazo.CancelAfter(TimeSpan.FromSeconds(10));
        using var saude = await cliente.GetAsync("/health", prazo.Token);
        Assert.Equal(HttpStatusCode.OK, saude.StatusCode);
        using var resposta = await cliente.PostAsJsonAsync("/api/v1/usuarios", Cadastro(), prazo.Token);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var id = (await resposta.Content.ReadFromJsonAsync<JsonElement>(prazo.Token)).GetProperty("id").GetGuid();
        await EsperarAsync(async () =>
        {
            await using var contexto = banco.CriarContexto();
            return await contexto.MensagensOutbox.AnyAsync(item => item.UserId == id && item.Attempts > 0, prazo.Token);
        }, prazo.Token);
        await using var leitura = banco.CriarContexto();
        Assert.Null((await leitura.MensagensOutbox.SingleAsync(item => item.UserId == id, prazo.Token)).PublishedAt);
        Assert.True(await leitura.Usuarios.AnyAsync(item => item.Id == id, prazo.Token));
        Assert.Equal(1, await leitura.LogsUsuarios.CountAsync(item => item.UsuarioId == id, prazo.Token));
    }

    [Fact]
    public async Task DesabilitadoNaoRegistraBusOuWorker()
    {
        await using var fabrica = new FabricaUsersApi(sobrescreverConfiguracao: new Dictionary<string, string?> { ["Outbox:Enabled"] = "false" });
        using var cliente = fabrica.CreateClient();
        Assert.Null(fabrica.Services.GetService<IBus>());
        Assert.Null(fabrica.Services.GetService<ProcessadorOutbox>());
        using var resposta = await cliente.GetAsync("/health", Cancelamento);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    private static object Cadastro() => new
    {
        nome = "Pessoa Sintética E12", cpf = DadosPersistencia.ProximoCpf(),
        email = $"e12-{Guid.NewGuid():N}@exemplo.test", dataNascimento = "2000-01-01", senha = "SenhaSintetica#E12"
    };

    private static async Task EsperarAsync(Func<Task<bool>> condicao, CancellationToken cancelamento)
    {
        while (!await condicao()) await Task.Delay(50, cancelamento);
    }
}
