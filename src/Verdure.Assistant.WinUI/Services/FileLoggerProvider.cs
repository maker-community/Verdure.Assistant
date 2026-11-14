using Microsoft.Extensions.Logging;
using System.Text;
using System.Threading.Channels;

namespace Verdure.Assistant.WinUI.Services;

/// <summary>
/// 简单的滚动文件日志：按天分文件、后台线程异步写入、自动清理旧日志。
/// 日志目录：%LOCALAPPDATA%\Verdure.Assistant\logs
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int RetainDays = 7;

    private readonly Channel<string> _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(4096)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true
    });
    private readonly Task _writer;
    private readonly string _directory;
    private readonly CancellationTokenSource _cts = new();

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Verdure.Assistant", "logs");

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        try
        {
            Directory.CreateDirectory(directory);
            CleanOldLogs();
        }
        catch
        {
            // 日志目录不可写时静默降级为不写文件
        }

        _writer = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _queue.Writer);

    private async Task WriteLoopAsync()
    {
        try
        {
            var buffer = new StringBuilder();
            while (await _queue.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
            {
                buffer.Clear();
                while (buffer.Length < 64 * 1024 && _queue.Reader.TryRead(out var line))
                {
                    buffer.AppendLine(line);
                }

                try
                {
                    var path = Path.Combine(_directory, $"assistant-{DateTime.Now:yyyyMMdd}.log");
                    await File.AppendAllTextAsync(path, buffer.ToString(), Encoding.UTF8).ConfigureAwait(false);
                }
                catch
                {
                    // 写入失败（文件被占用/磁盘满）时丢弃本批日志，不影响应用
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常关闭
        }
    }

    private void CleanOldLogs()
    {
        foreach (var file in Directory.EnumerateFiles(_directory, "assistant-*.log"))
        {
            if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-RetainDays))
            {
                try { File.Delete(file); } catch { /* 忽略 */ }
            }
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        try { _writer.Wait(TimeSpan.FromSeconds(2)); } catch { /* 忽略 */ }
        _cts.Cancel();
        _cts.Dispose();
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly ChannelWriter<string> _writer;

        public FileLogger(string category, ChannelWriter<string> writer)
        {
            _category = category;
            _writer = writer;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel.ToString()[..3].ToUpperInvariant()}] {_category}: {message}";
            if (exception != null)
            {
                line += Environment.NewLine + exception;
            }

            _writer.TryWrite(line);
        }
    }
}
