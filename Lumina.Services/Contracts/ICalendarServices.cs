using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Services.Contracts;

/// <summary>月历模型构建与按日查询。实现于 P1-05 / P1-06。</summary>
public interface ICalendarService
{
    CalendarMonthModel BuildMonth(int year, int month);

    Task<IReadOnlyList<ScheduleEvent>> EventsOnAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>区间查询；返回与 [start, end] 有交集的事件，含跨天日程。</summary>
    Task<IReadOnlyList<ScheduleEvent>> EventsBetweenAsync(
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default);
}

/// <summary>日程 CRUD。实现于 P1-06；编辑/删除默认作用于整个周期系列。</summary>
public interface IScheduleService
{
    Task<ScheduleEvent> AddAsync(ScheduleEvent item, CancellationToken cancellationToken = default);

    Task UpdateAsync(ScheduleEvent item, CancellationToken cancellationToken = default);

    Task DeleteAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);

    /// <summary>日程新增/修改/删除后触发，供日历页与小组件同步刷新。</summary>
    event Action? Changed;
}

/// <summary>待办全流程。实现于 P1-09；编辑必须保留完成态与完成时间。</summary>
public interface ITodoService
{
    Task<IReadOnlyList<TodoItem>> ListAsync(TodoFilter? filter = null, CancellationToken cancellationToken = default);

    /// <summary>按截止日期区间查询，供日历圆点、N+ 与当天待办使用。</summary>
    Task<IReadOnlyList<TodoItem>> TodosBetweenAsync(
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default);

    Task<TodoCompletionStats> CompletionStatsAsync(
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default);

    Task<TodoItem> AddAsync(TodoItem item, CancellationToken cancellationToken = default);

    Task UpdateAsync(TodoItem item, CancellationToken cancellationToken = default);

    Task ToggleDoneAsync(long id, CancellationToken cancellationToken = default);

    Task DeleteAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default);

    Task<TodoItem> ConvertFromNoteAsync(long noteId, CancellationToken cancellationToken = default);

    event Action? Changed;
}

/// <summary>完成风格策略；四个实现在 P1-10，视图不写四套分支。</summary>
public interface ICompletionStyleStrategy
{
    CompletionStyle Style { get; }

    CompletionVisual Render(bool done, bool reducedMotion);
}
