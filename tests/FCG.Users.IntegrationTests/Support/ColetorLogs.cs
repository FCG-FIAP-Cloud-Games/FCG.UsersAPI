using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace FCG.Users.IntegrationTests.Support;

internal sealed class ColetorLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _mensagens = new();
    public string Texto => string.Join(Environment.NewLine, _mensagens);
    public ILogger CreateLogger(string categoryName) => new Registrador(_mensagens);
    public void Dispose() { }

    private sealed class Registrador(ConcurrentQueue<string> mensagens) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            mensagens.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : Environment.NewLine + exception));
    }
}
