using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AbpAdmin.ClientErrors;

internal sealed class ClientErrorTestLogBuffer : ILoggerProvider, ILogger
{
    public static readonly ClientErrorTestLogBuffer Instance = new();

    private readonly ConcurrentQueue<string> _messages = new();

    public ILogger CreateLogger(string categoryName) => this;

    public void Dispose()
    {
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _messages.Enqueue(formatter(state, exception));
        if (exception != null)
        {
            _messages.Enqueue(exception.ToString());
        }
    }

    public bool Contains(string text)
    {
        foreach (var message in _messages)
        {
            if (message.Contains(text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
