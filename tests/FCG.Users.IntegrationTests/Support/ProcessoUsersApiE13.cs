using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Npgsql;

namespace FCG.Users.IntegrationTests.Support;

// Executa a API real em Kestrel, sem portas fixas, chaves pessoais ou ganchos no código de produção.
internal sealed class ProcessoUsersApiE13 : IAsyncDisposable
{
    private static readonly string[] PrefixosConfiguracao = ["Jwt__", "Outbox__", "RabbitMq__", "ConnectionStrings__", "Kestrel__", "Logging__", "AdministradorLocal__"];
    private readonly Process _processo;
    private readonly TaskCompletionSource<Uri> _endereco = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<string> _logs = new();
    internal HttpClient Cliente { get; private set; } = null!;
    internal string NomeAplicacaoBanco { get; }
    internal int Id => _processo.Id;
    internal bool Encerrado => _processo.HasExited;
    internal string Logs => string.Join(Environment.NewLine, _logs);

    private ProcessoUsersApiE13(string conexao, ChavesJwtTeste chaves, Dictionary<string, string?> configuracao, string? pastaPublicada)
    {
        NomeAplicacaoBanco = "e13-" + Guid.NewGuid().ToString("N");
        var assembly = typeof(ProcessoUsersApiE13).Assembly.Location;
        var inicio = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = pastaPublicada ?? AppContext.BaseDirectory
        };
        var argumentos = pastaPublicada is not null ? new[] { Path.Combine(pastaPublicada, "FCG.Users.Api.dll") } : new[] { "exec", "--runtimeconfig", Path.ChangeExtension(assembly, ".runtimeconfig.json"),
            "--depsfile", Path.ChangeExtension(assembly, ".deps.json"), typeof(global::Program).Assembly.Location };
        foreach (var argumento in argumentos)
            inicio.ArgumentList.Add(argumento);
        foreach (var chave in inicio.Environment.Keys.Where(ContemConfiguracaoExterna).ToArray()) inicio.Environment.Remove(chave);
        inicio.Environment["DOTNET_ENVIRONMENT"] = "Development";
        inicio.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        inicio.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:0";
        inicio.Environment["Swagger__Enabled"] = "false";
        inicio.Environment["Logging__LogLevel__Default"] = "Warning";
        inicio.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
        inicio.Environment["ConnectionStrings__UsersDatabase"] = new NpgsqlConnectionStringBuilder(conexao)
        { ApplicationName = NomeAplicacaoBanco, CommandTimeout = 60 }.ConnectionString;
        foreach (var item in chaves.CriarConfiguracao().Concat(configuracao))
            inicio.Environment[item.Key.Replace(":", "__", StringComparison.Ordinal)] = item.Value;
        _processo = new Process { StartInfo = inicio, EnableRaisingEvents = true };
        _processo.OutputDataReceived += (_, evento) => Capturar(evento.Data);
        _processo.ErrorDataReceived += (_, evento) => Capturar(evento.Data);
        _processo.Exited += (_, _) => _endereco.TrySetException(new InvalidOperationException("O processo da API encerrou antes de disponibilizar HTTP."));
    }

    internal static Task<ProcessoUsersApiE13> IniciarAsync(string conexao, ChavesJwtTeste chaves,
        Dictionary<string, string?> configuracao, CancellationToken cancelamento) =>
        IniciarInternoAsync(conexao, chaves, configuracao, null, cancelamento);

    internal static Task<ProcessoUsersApiE13> IniciarPublicadoAsync(string conexao, ChavesJwtTeste chaves,
        Dictionary<string, string?> configuracao, string pastaPublicada, CancellationToken cancelamento) =>
        IniciarInternoAsync(conexao, chaves, configuracao, pastaPublicada, cancelamento);

    private static async Task<ProcessoUsersApiE13> IniciarInternoAsync(string conexao, ChavesJwtTeste chaves,
        Dictionary<string, string?> configuracao, string? pastaPublicada, CancellationToken cancelamento)
    {
        var instancia = new ProcessoUsersApiE13(conexao, chaves, configuracao, pastaPublicada);
        try
        {
            if (!instancia._processo.Start()) throw new InvalidOperationException("Não foi possível iniciar a API de teste.");
            instancia._processo.BeginOutputReadLine();
            instancia._processo.BeginErrorReadLine();
            var endereco = await instancia._endereco.Task.WaitAsync(TimeSpan.FromSeconds(30), cancelamento);
            instancia.Cliente = new HttpClient { BaseAddress = endereco, Timeout = TimeSpan.FromSeconds(10) };
            using var resposta = await instancia.Cliente.GetAsync("/health", cancelamento);
            if (resposta.StatusCode != HttpStatusCode.OK) throw new InvalidOperationException("A API de teste não respondeu à verificação de vida.");
            return instancia;
        }
        catch { await instancia.DisposeAsync(); throw; }
    }

    private void Capturar(string? linha)
    {
        if (linha is null) return;
        _logs.Enqueue(linha);
        while (_logs.Count > 1000) _logs.TryDequeue(out _);
        const string marcador = "Now listening on: ";
        var posicao = linha.IndexOf(marcador, StringComparison.Ordinal);
        if (posicao >= 0 && Uri.TryCreate(linha[(posicao + marcador.Length)..].Trim(), UriKind.Absolute, out var endereco))
            _endereco.TrySetResult(endereco);
    }

    // Queda deliberada: mata apenas o PID criado por esta instância, sem enumerar outros processos.
    internal async Task DerrubarAsync()
    {
        if (!_processo.HasExited) _processo.Kill(entireProcessTree: true);
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _processo.WaitForExitAsync(prazo.Token);
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_processo.Id > 0) await DerrubarAsync(); }
        catch (InvalidOperationException) { /* Start pode ter falhado antes de existir um PID. */ }
        finally { Cliente?.Dispose(); _processo.Dispose(); }
    }

    private static bool ContemConfiguracaoExterna(string chave) =>
        PrefixosConfiguracao
            .Any(prefixo => chave.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase))
        || chave.Equals("ASPNETCORE_HTTP_PORTS", StringComparison.OrdinalIgnoreCase)
        || chave.Equals("ASPNETCORE_HTTPS_PORTS", StringComparison.OrdinalIgnoreCase);
}
