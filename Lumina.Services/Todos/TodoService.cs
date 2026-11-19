using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Domain.Stores;
using Lumina.Services.Contracts;
using Lumina.Services.Notes;

namespace Lumina.Services.Todos;

/// <summary>
/// P1-09 待办服务：新建/编辑/完成/删除、三组筛选与四种排序、逾期天数与完成率。
/// 编辑时保留原有完成态与完成时间，完成/取消完成统一走 ToggleDone。
/// </summary>
public sealed class TodoService : ITodoService
{
    private readonly ITodoStore _store;
    private readonly INoteStore _notes;
    private readonly Func<DateTimeOffset> _now;

    public TodoService(ITodoStore store, INoteStore notes, Func<DateTimeOffset>? now = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(notes);

        _store = store;
        _notes = notes;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public event Action? Changed;

    public Task<IReadOnlyList<TodoItem>> ListAsync(
        TodoFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        filter ??= new TodoFilter();
        var today = DateOnly.FromDateTime(_now().LocalDateTime);
        var weekStart = MondayOf(today);

        var items = _store.QueryAll()
            .Where(item => MatchesStatus(item, filter.Status, today))
            .Where(item => filter.Priority is not { } priority || item.Priority == priority)
            .Where(item => MatchesDateRange(item, filter.DateRange, today, weekStart))
            .ToArray();

        return Task.FromResult<IReadOnlyList<TodoItem>>(Sort(items, filter.Sort, today));
    }

    public Task<IReadOnlyList<TodoItem>> TodosBetweenAsync(
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        var items = _store.QueryRange(start, end);
        return Task.FromResult(items);
    }

    public Task<TodoCompletionStats> CompletionStatsAsync(
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        var items = _store.QueryAll()
            .Where(item => item.DueDate is { } dueDate
                ? dueDate >= start && dueDate <= end
                : DateOnly.FromDateTime(item.CreatedAt.LocalDateTime) >= start
                  && DateOnly.FromDateTime(item.CreatedAt.LocalDateTime) <= end)
            .ToArray();

        return Task.FromResult(new TodoCompletionStats(
            start,
            end,
            items.Count(item => item.Done),
            items.Length));
    }

    public Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(item);
        var now = _now();
        var created = normalized with
        {
            Id = _store.Insert(normalized with { CreatedAt = now, UpdatedAt = now }, now),
            CreatedAt = now,
            UpdatedAt = now,
        };

        Changed?.Invoke();
        return Task.FromResult(created);
    }

    public Task UpdateAsync(TodoItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Id <= 0)
        {
            throw new ArgumentException("更新待办需要有效的 id。", nameof(item));
        }

        var existing = _store.Find(item.Id)
            ?? throw new ArgumentException("待办不存在。", nameof(item));
        var now = _now();

        var updated = Normalize(item) with
        {
            Done = existing.Done,
            DoneAt = existing.DoneAt,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = now,
        };

        _store.Update(updated, now);
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task ToggleDoneAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = _store.Find(id)
            ?? throw new ArgumentException("待办不存在。", nameof(id));
        var now = _now();

        var updated = existing with
        {
            Done = !existing.Done,
            DoneAt = existing.Done ? null : now,
            UpdatedAt = now,
        };

        _store.Update(updated, now);
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        _store.Delete(ids);
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 便签转待办：标题取便签标题，缺失时取正文第一行（剥离清单标记），
    /// 回链写入 <see cref="TodoItem.NoteId"/>；同一条便签重复转换直接返回已存在的待办。
    /// </summary>
    public Task<TodoItem> ConvertFromNoteAsync(long noteId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (noteId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(noteId));
        }

        var existing = _store.QueryAll().FirstOrDefault(item => item.NoteId == noteId);
        if (existing is not null)
        {
            return Task.FromResult(existing);
        }

        var note = _notes.Find(noteId)
            ?? throw new ArgumentException("便签不存在。", nameof(noteId));

        var now = _now();
        var title = TodoTextFromNote(note);
        var todo = new TodoItem
        {
            Text = title,
            Note = string.Empty,
            NoteId = noteId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var id = _store.Insert(todo, now);
        Changed?.Invoke();
        return Task.FromResult(todo with { Id = id });
    }

    private static string TodoTextFromNote(NoteItem note)
    {
        if (!string.IsNullOrWhiteSpace(note.Title))
        {
            return note.Title.Trim();
        }

        foreach (var line in note.Content.Split('\n'))
        {
            if (ChecklistText.TryParseLine(line, out _, out _, out var body)
                && !string.IsNullOrWhiteSpace(body))
            {
                return body.Trim();
            }

            if (!string.IsNullOrWhiteSpace(line))
            {
                return line.Trim();
            }
        }

        return "未命名便签";
    }

    private static TodoItem Normalize(TodoItem item)
    {
        var text = item.Text.Trim();
        if (text.Length == 0)
        {
            throw new ArgumentException("待办标题不能为空。", nameof(item));
        }

        if (item.DueTime is not null && item.DueDate is null)
        {
            throw new ArgumentException("设置截止时间前请先设置截止日期。", nameof(item));
        }

        var note = item.Note.Trim();

        return item with
        {
            Text = text,
            Note = note,
            Done = item.Done,
            DoneAt = item.Done ? item.DoneAt : null,
        };
    }

    private static bool MatchesStatus(TodoItem item, TodoStatus status, DateOnly today) =>
        status switch
        {
            TodoStatus.Active => !item.Done,
            TodoStatus.Completed => item.Done,
            TodoStatus.Overdue => !item.Done && item.DueDate is { } dueDate && dueDate < today,
            _ => true,
        };

    private static bool MatchesDateRange(
        TodoItem item,
        TodoDateRange range,
        DateOnly today,
        DateOnly weekStart) =>
        range switch
        {
            TodoDateRange.Today => item.DueDate == today,
            TodoDateRange.ThisWeek => item.DueDate is { } due
                && due >= weekStart
                && due <= weekStart.AddDays(6),
            TodoDateRange.Overdue => !item.Done && item.DueDate is { } due && due < today,
            TodoDateRange.NoDueDate => item.DueDate is null,
            _ => true,
        };

    private static IReadOnlyList<TodoItem> Sort(
        IEnumerable<TodoItem> items,
        TodoSort sort,
        DateOnly today)
    {
        var source = items.OrderBy(item => item.Done);

        return sort switch
        {
            TodoSort.DueDate => source
                .ThenBy(item => item.DueDate is null ? 1 : 0)
                .ThenBy(item => item.DueDate)
                .ThenBy(item => item.DueTime ?? TimeOnly.MinValue)
                .ThenByDescending(item => item.CreatedAt)
                .ToArray(),

            TodoSort.Priority => source
                .ThenByDescending(item => item.Priority)
                .ThenBy(item => item.DueDate is null ? 1 : 0)
                .ThenBy(item => item.DueDate)
                .ThenByDescending(item => item.CreatedAt)
                .ToArray(),

            TodoSort.CreatedAt => source
                .ThenByDescending(item => item.CreatedAt)
                .ToArray(),

            _ => source
                .ThenByDescending(item => !item.Done && item.DueDate is { } due && due < today)
                .ThenBy(item => item.DueDate is null ? 1 : 0)
                .ThenBy(item => item.DueDate)
                .ThenByDescending(item => item.Priority)
                .ThenBy(item => item.DueTime ?? TimeOnly.MinValue)
                .ThenByDescending(item => item.CreatedAt)
                .ToArray(),
        };
    }

    private static DateOnly MondayOf(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }
}
