using Lumina.Domain.Enums;

namespace Lumina.Domain.Models;

/// <summary>剪贴板条目。文件类内容存路径，文本类存正文。</summary>
public sealed record ClipboardItem
{
    public long Id { get; init; }

    public string? LegacyId { get; init; }

    public ClipboardKind Kind { get; init; } = ClipboardKind.Text;

    public string? TextContent { get; init; }

    public string? FilePath { get; init; }

    public string? ThumbPath { get; init; }

    public string? Url { get; init; }

    public string? Mime { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? ContentHash { get; init; }

    public string? NormalizedText { get; init; }

    public bool Pinned { get; init; }

    public DateTimeOffset? PinnedAt { get; init; }

    public string? Note { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    public string? SourceWindow { get; init; }

    /// <summary>合并后的全部来源窗口（最新在前）；旧记录为空时由 SourceWindow 兜底。</summary>
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    public int DuplicateCount { get; init; } = 1;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>软删时间；非空表示已从列表与计数中排除，等待清理或恢复。</summary>
    public DateTimeOffset? DeletedAt { get; init; }
}

/// <summary>便签实体。位置与尺寸由小组件宿主管理，未定位时为 null。</summary>
public sealed record NoteItem
{
    public long Id { get; init; }

    public string? LegacyId { get; init; }

    public string? Title { get; init; }

    public required string Content { get; init; }

    public string? Color { get; init; }

    public double? X { get; init; }

    public double? Y { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }

    public bool Pinned { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>密码条目；敏感字段以密文保存，解密由 ICryptoProvider 负责。</summary>
public sealed record VaultEntry
{
    public long Id { get; init; }

    public VaultCategory Category { get; init; } = VaultCategory.Login;

    public required string Title { get; init; }

    public string? Username { get; init; }

    public byte[]? EncryptedPassword { get; init; }

    public string? Url { get; init; }

    public byte[]? EncryptedNote { get; init; }

    public bool Favorite { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public DateTimeOffset? UsedAt { get; init; }
}

/// <summary>专注会话；`SessionKey` 为幂等键，保证会话结束只写一次。</summary>
public sealed record FocusSession
{
    public required string SessionKey { get; init; }

    public FocusSource Source { get; init; } = FocusSource.Pomodoro;

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset EndedAt { get; init; }

    public required int DurationSeconds { get; init; }

    public bool Finished { get; init; } = true;

    public string? Note { get; init; }
}
