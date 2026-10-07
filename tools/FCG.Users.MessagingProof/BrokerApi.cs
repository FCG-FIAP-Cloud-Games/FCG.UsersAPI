using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FCG.Users.MessagingProof;

internal sealed class BrokerApi : IDisposable
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("http://127.0.0.1:15672/api/"), Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _vhost;

    public BrokerApi(string vhost)
    {
        _vhost = Uri.EscapeDataString(vhost);
        var credential = Encoding.UTF8.GetBytes($"{Ambiente("E10_ADMIN_USER")}:{Ambiente("E10_ADMIN_PASSWORD")}");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credential));
    }

    public static string Ambiente(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Configuração ausente: {name}");

    public async Task InicializarAsync(string vhost)
    {
        await PutAsync($"vhosts/{_vhost}", new { });
        foreach (var prefix in new[] { "USERS", "NOTIFICATIONS" })
        {
            var user = Ambiente($"E10_{prefix}_USER");
            await PutAsync($"users/{Uri.EscapeDataString(user)}", new { password = Ambiente($"E10_{prefix}_PASSWORD"), tags = "" });
            // Credenciais separadas limitadas ao vhost descartável; matriz fina fica na infraestrutura.
            await PutAsync($"permissions/{_vhost}/{Uri.EscapeDataString(user)}", new { configure = ".*", write = ".*", read = ".*" });
        }
        if (string.IsNullOrWhiteSpace(vhost)) throw new InvalidOperationException("Vhost vazio.");
    }

    public async Task<JsonElement?> GetAsync(string path)
    {
        using var response = await _http.GetAsync(path);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.Clone();
    }

    public Task<JsonElement?> FilaAsync(string name) => GetAsync($"queues/{_vhost}/{Uri.EscapeDataString(name)}");
    public Task<JsonElement?> ExchangeAsync(string name) => GetAsync($"exchanges/{_vhost}/{Uri.EscapeDataString(name)}");
    public Task<JsonElement?> VisaoGeralAsync() => GetAsync("overview");

    public async Task ProvisionarAsync(string exchange, string queue)
    {
        await PutAsync($"exchanges/{_vhost}/{Uri.EscapeDataString(exchange)}", new { type = "fanout", durable = true, auto_delete = false, @internal = false, arguments = new { } });
        await PutAsync($"exchanges/{_vhost}/{Uri.EscapeDataString(queue)}", new { type = "fanout", durable = true, auto_delete = false, @internal = false, arguments = new { } });
        await PutAsync($"queues/{_vhost}/{Uri.EscapeDataString(queue)}", new { durable = true, auto_delete = false, arguments = new { } });
        await VincularAsync(exchange, queue);
        await PostAsync($"bindings/{_vhost}/e/{Uri.EscapeDataString(queue)}/q/{Uri.EscapeDataString(queue)}", new { routing_key = "", arguments = new { } });
    }

    public Task VincularAsync(string source, string destination) => PostAsync(
        $"bindings/{_vhost}/e/{Uri.EscapeDataString(source)}/e/{Uri.EscapeDataString(destination)}", new { routing_key = "", arguments = new { } });

    public async Task DesvincularAsync(string source, string destination)
    {
        var path = $"bindings/{_vhost}/e/{Uri.EscapeDataString(source)}/e/{Uri.EscapeDataString(destination)}";
        var bindings = await GetAsync(path) ?? throw new InvalidOperationException("Binding ausente.");
        foreach (var binding in bindings.EnumerateArray())
        {
            var key = binding.GetProperty("properties_key").GetString() ?? throw new InvalidOperationException("Binding sem chave.");
            using var response = await _http.DeleteAsync($"{path}/{Uri.EscapeDataString(key)}");
            response.EnsureSuccessStatusCode();
        }
    }

    // Inspeciona e recoloca a mensagem; não a retira definitivamente da fila.
    public async Task<JsonElement> EspiarAsync(string queue)
    {
        using var response = await _http.PostAsJsonAsync($"queues/{_vhost}/{Uri.EscapeDataString(queue)}/get", new { count = 1, ackmode = "ack_requeue_true", encoding = "base64", truncate = 50000 });
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return document.RootElement.EnumerateArray().Single().Clone();
    }

    private async Task PutAsync(string path, object value)
    {
        using var response = await _http.PutAsJsonAsync(path, value);
        response.EnsureSuccessStatusCode();
    }

    private async Task PostAsync(string path, object value)
    {
        using var response = await _http.PostAsJsonAsync(path, value);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
