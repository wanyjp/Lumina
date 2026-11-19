using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Lumina.Services.Logging;

/// <summary>滚动文件日志选项；默认 5 MB 滚动、保留 3 个历史文件。</summary>
public sealed record RollingFileLoggerOptions
{
    public required string Directory { get; init; }

    public string FileName { get; init; } = "app.log";

    public long MaxFileBytes { get; init; } = 5 * 1024 * 1024;

    public int RetainedFiles { get; init; } = 3;

    public LogLevel MinimumLevel { get; init; } = LogLevel.Information;
}

/// <summary>
/// 结构化滚动文件日志：每条记录一行 JSON，含时间、级别、分类、事件与异常。
/// 写入失败只吞掉日志异常，不影响主流程。
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly RollingFileLoggerOptions _options;
    private readonly object _gate = new();
    private StreamWriter? _writer;
    private DateTimeOffset _nextRotateAttempt = DateTimeOffset.MinValue;
    private bool _disposed;

    public RollingFileLoggerProvider(RollingFileLoggerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.Directory))
        {
            throw new ArgumentException("日志目录不能为空。", nameof(options));
        }
    }

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(this, categoryName);

    internal bool IsEnabled(LogLevel level) =>
        !_disposed && level != LogLevel.None && level >= _options.MinimumLevel;

    internal void Write(LogLevel level, string category, EventId eventId, string message, Exception? exception)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        try
        {
            var record = JsonSerializer.Serialize(
                new LogRecord(
                    DateTimeOffset.Now.ToString("O"),
                    level.ToString(),
                    category,
                    eventId.Id,
                    eventId.Name,
                    message,
                    exception?.ToString()),
                JsonOptions);

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                EnsureWriter();
                _writer!.WriteLine(record);
                _writer.Flush();

                if (_writer.BaseStream.Length >= _options.MaxFileBytes
                    && DateTimeOffset.Now >= _nextRotateAttempt)
                {
                    Rotate();
                }
            }
        }
        catch (Exception diagnostics)
        {
            // 日志写入失败不得影响主流程；调试期可在 Debugger 中观察。
            System.Diagnostics.Debug.WriteLine(diagnostics);
        }
    }

    private void EnsureWriter()
    {
        if (_writer is not null)
        {
            return;
        }

        Directory.CreateDirectory(_options.Directory);
        var path = Path.Combine(_options.Directory, _options.FileName);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        // 托盘 / 启动类故障往往需要强杀进程后取证；不自动 flush 会让最后一段日志随进程消失。
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
    }

    private void Rotate()
    {
        _writer?.Dispose();
        _writer = null;

        var path = Path.Combine(_options.Directory, _options.FileName);

        try
        {
            for (var index = _options.RetainedFiles - 1; index >= 1; index--)
            {
                var source = $"{path}.{index}";
                var target = $"{path}.{index + 1}";
                if (File.Exists(source))
                {
                    File.Move(source, target, overwrite: true);
                }
            }

            if (File.Exists(path))
            {
                File.Move(path, $"{path}.1", overwrite: true);
            }

            _nextRotateAttempt = DateTimeOffset.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 滚动失败（历史文件被占用等）：继续追加当前文件，一分钟后再试，
            // 避免每次写入都重复失败的移动操作。
            _nextRotateAttempt = DateTimeOffset.Now.AddMinutes(1);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed record LogRecord(
        string Timestamp,
        string Level,
        string Category,
        int EventId,
        string? EventName,
        string Message,
        string? Exception);
}

internal sealed class RollingFileLogger : ILogger
{
    private readonly RollingFileLoggerProvider _provider;
    private readonly string _category;

    public RollingFileLogger(RollingFileLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
    }

    public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

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

        var message = formatter(state, exception);
        _provider.Write(logLevel, _category, eventId, message, exception);
    }
}
