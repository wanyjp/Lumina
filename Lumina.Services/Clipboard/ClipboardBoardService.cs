using Lumina.Domain.Clipboard;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Clipboard;

/// <summary>删除结果：固定条目会被跳过，由界面提示先取消固定。</summary>
public readonly record struct ClipboardDeleteOutcome(int Deleted, int SkippedPinned);

/// <summary>回贴结果：失败时 <see cref="Message"/> 为降级提示文案，绝不静默失败。</summary>
public readonly record struct ClipboardCopyOutcome(bool Success, bool MovedToTop, string Message);

/// <summary>备注与标签保存结果；<see cref="Dropped"/> 为被截断/去重/超限丢弃的个数。</summary>
public readonly record struct ClipboardAnnotationOutcome(
    bool Success,
    IReadOnlyList<string> Tags,
    int Dropped,
    string? Message);

/// <summary>内容编辑结果；<see cref="Kind"/> 为保存后重新判定的类型。</summary>
public readonly record struct ClipboardContentOutcome(
    bool Success,
    ClipboardKind Kind,
    string? Message);

/// <summary>
/// P2-03 剪贴板看板编排：列表查询（搜索/页签/标签）、固定与取消固定、
/// 单条与多选清理、备注标签保存、回贴并前移置顶。UI 只消费本服务，不直接访问仓储。
/// </summary>
public sealed class ClipboardBoardService
{
    private readonly IClipboardStore _store;
    private readonly ClipboardRetentionService _retention;
    private readonly IClipboardWriter _writer;
    private readonly ISettingsStore _settings;

    public ClipboardBoardService(
        IClipboardStore store,
        ClipboardRetentionService retention,
        IClipboardWriter writer,
        ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(retention);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(settings);

        _store = store;
        _retention = retention;
        _writer = writer;
        _settings = settings;
    }

    /// <summary>标签栏是否显示；关闭后界面同时收起标签筛选。</summary>
    public bool TagsVisible => _settings.Get(
        PreferenceKeys.ClipboardShowTags,
        PreferenceDefaults.ClipboardShowTags);

    /// <summary>按页签、搜索与标签读取列表；一次读取后纯内存筛选，避免逐键全表扫描。</summary>
    public ClipboardListView Load(
        ClipboardFilter filter = ClipboardFilter.All,
        string? search = null,
        string? tag = null)
    {
        var items = _store.QueryRecent(EvictionPolicy.MaxLimit);
        return ClipboardQuery.Build(items, filter, search, tag);
    }

    /// <summary>固定或取消固定；固定项不参与淘汰计数。</summary>
    public bool SetPinned(long id, bool pinned) => pinned
        ? _retention.Pin(id)
        : _retention.Unpin(id);

    /// <summary>
    /// 删除指定条目：固定项一律跳过，其余按软删处理（30 天内可恢复，附件随过期清理解除）。
    /// </summary>
    public ClipboardDeleteOutcome Delete(IEnumerable<ClipboardItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var deleted = 0;
        var skippedPinned = 0;

        foreach (var item in items)
        {
            if (item.Pinned)
            {
                skippedPinned++;
                continue;
            }

            if (_retention.SoftDelete(item.Id))
            {
                deleted++;
            }
        }

        return new ClipboardDeleteOutcome(deleted, skippedPinned);
    }

    /// <summary>保存备注与标签；标签按「最多 5 个、每个 12 字」截断去重。</summary>
    public ClipboardAnnotationOutcome SaveAnnotation(
        ClipboardItem item,
        string? note,
        string? tagInput)
    {
        ArgumentNullException.ThrowIfNull(item);

        var tags = ClipboardTagPolicy.FromInput(tagInput, out var dropped);
        var updated = _store.UpdateAnnotation(item.Id, note, tags);
        return updated is null
            ? new ClipboardAnnotationOutcome(false, tags, dropped, "条目已不存在，可能已被清理。")
            : new ClipboardAnnotationOutcome(true, tags, dropped, null);
    }

    /// <summary>
    /// 编辑文本类条目的正文：重新按内容判定类型并重算归一化文本与哈希；
    /// 链接改为普通文本、文本改为链接都会同步更新分类。图片/文件不支持编辑正文。
    /// </summary>
    public ClipboardContentOutcome SaveContent(ClipboardItem item, string? text)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Kind is not (ClipboardKind.Text or ClipboardKind.Link or ClipboardKind.Html))
        {
            return new ClipboardContentOutcome(
                false,
                item.Kind,
                "图片与文件条目不能编辑正文；可改为编辑备注与标签。");
        }

        var content = (text ?? string.Empty).Trim();
        if (content.Length == 0)
        {
            return new ClipboardContentOutcome(false, item.Kind, "内容不能为空。");
        }

        var kind = item.Kind == ClipboardKind.Html
            ? ClipboardKind.Html
            : ClipClassifier.Classify(content, null, null, hasImage: false);
        var normalized = ClipClassifier.NormalizeText(content);
        var canonical = kind == ClipboardKind.Text ? normalized : content;
        if (canonical.Length == 0)
        {
            return new ClipboardContentOutcome(false, item.Kind, "内容不能为空。");
        }

        var updated = _store.UpdateContent(
            item.Id,
            item with
            {
                Kind = kind,
                TextContent = content,
                Url = kind == ClipboardKind.Link ? content : null,
                Mime = kind switch
                {
                    ClipboardKind.Link => "text/uri-list",
                    ClipboardKind.Html => "text/html",
                    _ => "text/plain; charset=utf-8",
                },
                NormalizedText = kind == ClipboardKind.Text ? normalized : null,
                ContentHash = ClipClassifier.ComputeTextHash(kind, canonical),
            },
            DateTimeOffset.Now);

        return updated is null
            ? new ClipboardContentOutcome(false, kind, "条目已不存在，可能已被清理。")
            : new ClipboardContentOutcome(true, kind, null);
    }

    /// <summary>
    /// 回贴：写入系统剪贴板，成功后刷新更新时间把条目前移置顶。
    /// 写入失败返回降级提示，不改变列表顺序。
    /// </summary>
    public ClipboardCopyOutcome Copy(ClipboardItem item, bool plainText)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (plainText)
        {
            var text = ClipboardPlainText.Of(item);
            if (string.IsNullOrEmpty(text))
            {
                return new ClipboardCopyOutcome(
                    false,
                    false,
                    "图片条目没有可复制的纯文本，请使用「复制到剪贴板」。");
            }

            if (!_writer.TryWritePlainText(text))
            {
                return new ClipboardCopyOutcome(false, false, FailureHint);
            }
        }
        else if (!_writer.TryWrite(item))
        {
            return new ClipboardCopyOutcome(false, false, FailureHint);
        }

        var moved = _store.Touch(item.Id, DateTimeOffset.Now) is not null;
        return new ClipboardCopyOutcome(true, moved, "已复制到剪贴板。");
    }

    /// <summary>回贴失败时的降级提示：引导手动复制，不掩盖失败。</summary>
    public const string FailureHint =
        "写入系统剪贴板失败（可能被其他程序占用）。请展开详情后手动选择内容并按 Ctrl+C。";
}
