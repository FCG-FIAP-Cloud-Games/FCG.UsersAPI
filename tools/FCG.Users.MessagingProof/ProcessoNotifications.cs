using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace FCG.Users.MessagingProof;

internal sealed class ProcessoNotifications : IDisposable
{
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly Process _process;

    public ProcessoNotifications(string dll, string vhost)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(dll)!
        };
        start.ArgumentList.Add(dll);
        start.Environment["ASPNETCORE_URLS"] = "http://127.0.0.1:5089";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["RabbitMq__Host"] = "127.0.0.1";
        start.Environment["RabbitMq__VirtualHost"] = vhost;
        start.Environment["RabbitMq__Username"] = BrokerApi.Ambiente("E10_NOTIFICATIONS_USER");
        start.Environment["RabbitMq__Password"] = BrokerApi.Ambiente("E10_NOTIFICATIONS_PASSWORD");
        _process = new Process { StartInfo = start };
        _process.OutputDataReceived += (_, args) => { if (args.Data is not null) _lines.Enqueue(args.Data); };
        _process.ErrorDataReceived += (_, args) => { if (args.Data is not null) _lines.Enqueue(args.Data); };
        if (!_process.Start()) throw new InvalidOperationException("Notifications não iniciou.");
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public int Concluidos(Guid eventId) => _lines.Count(line => line.Contains("UserCreatedEvent processado |", StringComparison.Ordinal)
        && line.Contains(eventId.ToString(), StringComparison.OrdinalIgnoreCase));

    public int Invalidos(Guid eventId) => _lines.Count(line => line.Contains("Payload ", StringComparison.Ordinal) && line.Contains("para UserCreatedEvent |", StringComparison.Ordinal)
        && line.Contains(eventId.ToString(), StringComparison.OrdinalIgnoreCase));

    public bool Encerrou => _process.HasExited;

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10000);
        }
        _process.Dispose();
        // Logs brutos, inclusive simulação com e-mail, ficam apenas em memória.
    }
}
