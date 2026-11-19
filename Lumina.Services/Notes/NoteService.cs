using Lumina.Domain.Models;
using Lumina.Domain.Notes;
using Lumina.Domain.Stores;
using Lumina.Services.Contracts;

namespace Lumina.Services.Notes;

/// <summary>
/// P3-05 便签服务：标题空白归一为 null、正文统一 LF、颜色归一到色卡键、
/// 位置尺寸做合法性收敛；新增时补时间戳，更新时保留创建时间。
/// 自动保存的 800ms 防抖在编辑窗里做，服务只负责一次落库。
/// </summary>
public sealed class NoteService : INoteService
{
    private const double MinSize = 160;
    private const double MaxSize = 1600;

    private readonly INoteStore _store;
    private readonly Func<DateTimeOffset> _now;

    public NoteService(INoteStore store, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public event Action? Changed;

    public Task<IReadOnlyList<NoteItem>> AllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.QueryAll());
    }

    public Task<NoteItem> SaveAsync(NoteItem note, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _now();
        var normalized = Normalize(note);

        if (normalized.Id > 0)
        {
            var existing = _store.Find(normalized.Id)
                ?? throw new ArgumentException("便签不存在。", nameof(note));
            var updated = normalized with
            {
                LegacyId = normalized.LegacyId ?? existing.LegacyId,
                CreatedAt = existing.CreatedAt,
                UpdatedAt = now,
            };
            _store.Update(updated, now);
            Changed?.Invoke();
            return Task.FromResult(updated);
        }

        var created = normalized with
        {
            CreatedAt = now,
            UpdatedAt = now,
        };
        var id = _store.Insert(created, now);
        Changed?.Invoke();
        return Task.FromResult(created with { Id = id });
    }

    public Task DeleteAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        cancellationToken.ThrowIfCancellationRequested();

        if (_store.Delete(ids) > 0)
        {
            Changed?.Invoke();
        }

        return Task.CompletedTask;
    }

    /// <summary>标题去空白、正文统一 LF、颜色归一、尺寸夹在合理区间。</summary>
    public static NoteItem Normalize(NoteItem note)
    {
        var title = string.IsNullOrWhiteSpace(note.Title) ? null : note.Title.Trim();
        var content = (note.Content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        return note with
        {
            Title = title,
            Content = content,
            Color = NotePalette.Normalize(note.Color),
            Width = ClampSize(note.Width),
            Height = ClampSize(note.Height),
            X = note.X is { } x && double.IsFinite(x) ? x : null,
            Y = note.Y is { } y && double.IsFinite(y) ? y : null,
        };
    }

    private static double? ClampSize(double? value)
    {
        if (value is not { } size || !double.IsFinite(size))
        {
            return null;
        }

        return Math.Clamp(size, MinSize, MaxSize);
    }
}
