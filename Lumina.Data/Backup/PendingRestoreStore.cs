using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lumina.Data.Backup;

/// <summary>写入磁盘的待恢复请求。</summary>
public sealed record PendingRestoreRequest(string BackupPath, DateTimeOffset RequestedAt);

/// <summary>
/// 待恢复请求的持久化：单文件 JSON，写入走“临时文件 + 替换”，
/// 避免应用在写一半时退出导致下次启动读到损坏内容。
/// </summary>
public sealed class PendingRestoreStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _filePath;

    public PendingRestoreStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public string FilePath => _filePath;

    public PendingRestoreRequest? Read()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var request = JsonSerializer.Deserialize<PendingRestoreRequest>(json, SerializerOptions);
            return string.IsNullOrWhiteSpace(request?.BackupPath) ? null : request;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 请求文件损坏等同于没有请求：不能让一次坏标记阻断启动。
            return null;
        }
    }

    public void Write(string backupPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);

        var directory = Path.GetDirectoryName(_filePath) ?? ".";
        Directory.CreateDirectory(directory);

        var request = new PendingRestoreRequest(
            Path.GetFullPath(backupPath),
            DateTimeOffset.Now);
        var json = JsonSerializer.Serialize(request, SerializerOptions);

        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    internal static string Describe(PendingRestoreRequest request) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(request.BackupPath)}（{request.RequestedAt:yyyy-MM-dd HH:mm}）");
}
