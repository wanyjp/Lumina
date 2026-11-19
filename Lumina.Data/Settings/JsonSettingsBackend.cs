using System.Globalization;
using System.Text.Json;
using Lumina.Domain.Stores;

namespace Lumina.Data.Settings;

/// <summary>
/// 配置的 JSON 后端：写入 <c>config/settings.json</c>，用于便携/缓存场景。
/// 变更先驻留内存，<see cref="SaveAsync"/> 以临时文件替换方式原子落盘。
/// </summary>
public sealed class JsonSettingsBackend : ISettingsBackend
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly Dictionary<string, SettingsEntry> _entries;
    private long _revision;
    private long _savedRevision;

    public JsonSettingsBackend(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _entries = Load();
        _revision = 0;
        _savedRevision = 0;
    }

    public string FilePath => _filePath;

    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            return _entries.TryGetValue(key, out var entry) ? entry.Value : null;
        }
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        lock (_gate)
        {
            return _entries.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);
        }
    }

    public void Set(string key, string value, string? module)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        lock (_gate)
        {
            _entries[key] = new SettingsEntry(value, module);
            _revision++;
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        SettingsDocument document;
        long revision;
        lock (_gate)
        {
            if (_revision == _savedRevision)
            {
                return;
            }

            revision = _revision;
            document = new SettingsDocument(
                Version: 1,
                UpdatedAt: DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Settings: new Dictionary<string, SettingsEntry>(_entries, StringComparer.Ordinal));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath) ?? ".");

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var tempPath = _filePath + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            // 写入或替换失败时清理临时文件，避免下次启动留下 .tmp 残留。
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 清理失败不掩盖主异常。
            }

            throw;
        }

        lock (_gate)
        {
            if (revision == _revision)
            {
                _savedRevision = revision;
            }
        }
    }

    private Dictionary<string, SettingsEntry> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, SettingsEntry>(StringComparer.Ordinal);
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var document = JsonSerializer.Deserialize<SettingsDocument>(json, JsonOptions);
            return document?.Settings is null
                ? new Dictionary<string, SettingsEntry>(StringComparer.Ordinal)
                : new Dictionary<string, SettingsEntry>(document.Settings, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            PreserveCorruptFile();
            return new Dictionary<string, SettingsEntry>(StringComparer.Ordinal);
        }
    }

    private void PreserveCorruptFile()
    {
        try
        {
            var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.Move(_filePath, $"{_filePath}.corrupt-{stamp}", overwrite: true);
        }
        catch (IOException)
        {
            // 保留失败时仍按空配置启动，下一次保存会覆盖损坏文件。
        }
    }

    private sealed record SettingsDocument(int Version, string UpdatedAt, Dictionary<string, SettingsEntry> Settings);

    private sealed record SettingsEntry(string Value, string? Module);
}
