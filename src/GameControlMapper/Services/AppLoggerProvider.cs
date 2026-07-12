using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class AppLoggerProvider : ILoggerProvider
{
    private readonly AppLogSink _sink;
    private readonly FileLogSink _file;

    public AppLoggerProvider(AppLogSink sink, FileLogSink file)
    {
        _sink = sink;
        _file = file;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new AppLogger(categoryName, _sink, _file);
    }

    public void Dispose()
    {
    }

    private sealed class AppLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly AppLogSink _sink;
        private readonly FileLogSink _file;

        public AppLogger(string categoryName, AppLogSink sink, FileLogSink file)
        {
            _categoryName = categoryName;
            _sink = sink;
            _file = file;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var shortCategory = _categoryName.Split('.').LastOrDefault() ?? _categoryName;
            var text = formatter(state, exception);
            if (exception is not null)
            {
                text = $"{text}: {exception.Message}";
            }

            _sink.Add($"[{logLevel}] {shortCategory}: {text}");
            _file.Write(DateTimeOffset.Now,logLevel.ToString(),shortCategory,formatter(state,exception),exception);
        }
    }
}
