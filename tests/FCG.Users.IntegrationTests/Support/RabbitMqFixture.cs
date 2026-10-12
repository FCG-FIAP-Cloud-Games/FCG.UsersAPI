using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FCG.Users.Application.Messaging;

namespace FCG.Users.IntegrationTests.Support;

// MassTransit 8.3 mantém um fallback global de logs. Outros hosts de teste não devem encerrá-lo durante estas provas.
[CollectionDefinition(Nome, DisableParallelization = true)]
public sealed class ColecaoMensageria : ICollectionFixture<RabbitMqFixture>, ICollectionFixture<BancoUsersFixture>
{
    public const string Nome = "E12 PostgreSQL e RabbitMQ";
}

public class RabbitMqFixture : IAsyncLifetime
{
    public const string Imagem = "rabbitmq@sha256:8dd6e3570ddaa2ef82a6c3a8950e79c1f73893adfbdddbc374fc1f1ac8a0f5dd";
    private readonly string _senha = Guid.NewGuid().ToString("N");
    private readonly IContainer _container;
    private HttpClient? _http;
    internal string Host => _container.Hostname;
    internal ushort Porta => _container.GetMappedPublicPort(5672);
    internal string IdContainer => _container.Id;
    internal Task PararAsync(CancellationToken cancelamento) => _container.StopAsync(cancelamento);
    internal Task RetomarAsync(CancellationToken cancelamento) => _container.StartAsync(cancelamento);
    internal string? Versao { get; private set; }

    public RabbitMqFixture() : this(preservarPortas: false) { }

    protected RabbitMqFixture(bool preservarPortas)
    {
        var builder = new ContainerBuilder(Imagem)
            .WithEnvironment("RABBITMQ_DEFAULT_USER", "e12_admin")
            .WithEnvironment("RABBITMQ_DEFAULT_PASS", _senha);
        // O Docker pode reatribuir portas com HostPort=0 em stop/start. E13 conserva o endpoint.
        if (preservarPortas)
        {
            var portas = PortasLivres();
            builder = builder.WithPortBinding(portas.Amqp, 5672).WithPortBinding(portas.Gerenciamento, 15672);
        }
        else builder = builder.WithPortBinding(5672, true).WithPortBinding(15672, true);
        _container = builder.WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(requisicao =>
                requisicao.ForPort(15672).ForPath("/api/overview").WithBasicAuthentication("e12_admin", _senha)))
            .Build();
    }

    private static (int Amqp, int Gerenciamento) PortasLivres()
    {
        var amqp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        var gerenciamento = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        try
        {
            amqp.Start(); gerenciamento.Start();
            return (((IPEndPoint)amqp.LocalEndpoint).Port, ((IPEndPoint)gerenciamento.LocalEndpoint).Port);
        }
        finally { amqp.Stop(); gerenciamento.Stop(); }
    }

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
        _http = new HttpClient
        {
            BaseAddress = new UriBuilder("http", Host, _container.GetMappedPublicPort(15672), "api/").Uri,
            Timeout = TimeSpan.FromSeconds(10)
        };
        _http.DefaultRequestHeaders.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("e12_admin:" + _senha)));
        using var resposta = await _http.GetAsync("overview", TestContext.Current.CancellationToken);
        resposta.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Versao = json.RootElement.GetProperty("rabbitmq_version").GetString();
    }

    internal async Task<CenarioRabbit> NovoCenarioAsync(bool provisionar = true)
    {
        var cenario = new CenarioRabbit(this, "e12-" + Guid.NewGuid().ToString("N"));
        await PutAsync("vhosts/" + cenario.VirtualHost, new { });
        await PutAsync("users/" + cenario.Username, new { password = cenario.Password, tags = "" });
        await PutAsync($"permissions/{cenario.VirtualHost}/{cenario.Username}", new { configure = ".*", write = ".*", read = ".*" });
        if (provisionar) await cenario.ProvisionarAsync();
        return cenario;
    }

    internal async Task PutAsync(string caminho, object valor)
    {
        using var resposta = await _http!.PutAsJsonAsync(caminho, valor, TestContext.Current.CancellationToken);
        resposta.EnsureSuccessStatusCode();
    }

    internal async Task PostAsync(string caminho, object valor)
    {
        using var resposta = await _http!.PostAsJsonAsync(caminho, valor, TestContext.Current.CancellationToken);
        resposta.EnsureSuccessStatusCode();
    }

    internal async Task<JsonElement?> GetAsync(string caminho)
    {
        using var resposta = await _http!.GetAsync(caminho, TestContext.Current.CancellationToken);
        if (resposta.StatusCode == HttpStatusCode.NotFound) return null;
        resposta.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.Clone();
    }

    internal async Task<JsonElement> MensagemAsync(string vhost, string fila, bool remover, int quantidade = 1)
    {
        using var resposta = await _http!.PostAsJsonAsync($"queues/{vhost}/{Uri.EscapeDataString(fila)}/get",
            new { count = quantidade, ackmode = remover ? "ack_requeue_false" : "ack_requeue_true", encoding = "base64", truncate = 50000 }, TestContext.Current.CancellationToken);
        resposta.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return json.RootElement.Clone();
    }

    internal async Task DeleteAsync(string caminho)
    {
        using var resposta = await _http!.DeleteAsync(caminho, TestContext.Current.CancellationToken);
        resposta.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        _http?.Dispose();
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    internal sealed class CenarioRabbit(RabbitMqFixture fixture, string virtualHost) : IAsyncDisposable
    {
        internal const string Fila = "notifications-user-created";
        internal static string Exchange => DefinicaoUserCreatedEvent.Tipo["urn:message:".Length..];
        internal string VirtualHost { get; } = virtualHost;
        internal string Username { get; } = "e12_produtor_" + Guid.NewGuid().ToString("N");
        internal string Password { get; } = Guid.NewGuid().ToString("N");

        internal Dictionary<string, string?> Configuracao => new(StringComparer.Ordinal)
        {
            ["Outbox:Enabled"] = "true", ["Outbox:PollIntervalSeconds"] = "0.1",
            ["Outbox:PublishTimeoutSeconds"] = "2", ["Outbox:InitialRetrySeconds"] = "0.2",
            ["Outbox:MaxRetrySeconds"] = "1", ["Outbox:BatchSize"] = "20",
            ["RabbitMq:Host"] = fixture.Host, ["RabbitMq:Port"] = fixture.Porta.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:VirtualHost"] = VirtualHost, ["RabbitMq:Username"] = Username, ["RabbitMq:Password"] = Password
        };

        internal async Task ProvisionarAsync()
        {
            await fixture.PutAsync($"exchanges/{VirtualHost}/{Uri.EscapeDataString(Exchange)}",
                new { type = "fanout", durable = true, auto_delete = false, @internal = false, arguments = new { } });
            await fixture.PutAsync($"exchanges/{VirtualHost}/{Fila}",
                new { type = "fanout", durable = true, auto_delete = false, @internal = false, arguments = new { } });
            await fixture.PutAsync($"queues/{VirtualHost}/{Fila}",
                new { durable = true, auto_delete = false, arguments = new { } });
            await fixture.PostAsync($"bindings/{VirtualHost}/e/{Fila}/q/{Fila}", new { routing_key = "", arguments = new { } });
            await VincularAsync();
        }

        internal Task VincularAsync() => fixture.PostAsync(
            $"bindings/{VirtualHost}/e/{Uri.EscapeDataString(Exchange)}/e/{Fila}", new { routing_key = "", arguments = new { } });

        internal async Task DesvincularAsync()
        {
            var caminho = $"bindings/{VirtualHost}/e/{Uri.EscapeDataString(Exchange)}/e/{Fila}";
            var bindings = await fixture.GetAsync(caminho) ?? throw new InvalidOperationException("Binding ausente.");
            foreach (var binding in bindings.EnumerateArray())
                await fixture.DeleteAsync(caminho + "/" + Uri.EscapeDataString(binding.GetProperty("properties_key").GetString()!));
        }

        internal Task<JsonElement> MensagemAsync(bool remover = false) => fixture.MensagemAsync(VirtualHost, Fila, remover);
        internal Task<JsonElement> MensagensAsync(int quantidade = 20, bool remover = false) => fixture.MensagemAsync(VirtualHost, Fila, remover, quantidade);
        internal Task<JsonElement?> FilaAsync() => fixture.GetAsync($"queues/{VirtualHost}/{Fila}");

        public async ValueTask DisposeAsync()
        {
            await fixture.DeleteAsync("vhosts/" + VirtualHost);
            await fixture.DeleteAsync("users/" + Username);
        }
    }
}


public sealed class RabbitMqRecuperacaoFixture : RabbitMqFixture
{
    public RabbitMqRecuperacaoFixture() : base(preservarPortas: true) { }
}
