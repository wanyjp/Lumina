using Lumina.Domain.Models;
using Lumina.Services.Contracts;

namespace Lumina.Services.Calendar;

/// <summary>单日密度：条目总数、可见色条、是否显示 N+ 与隐藏条数。</summary>
public sealed record CalendarDayDensity(
    int ItemCount,
    IReadOnlyList<CalendarBarModel> Bars,
    bool HasMore,
    int HiddenCount);

/// <summary>
/// 月历密度排版规则：色条最多 2 档，条目超过 3 条时显示 N+；
/// 基础行高横版 40、竖版 46，每档色条 +4，出现 N+ 再 +14。
/// </summary>
public static class CalendarDensityBuilder
{
    public const int MaxBars = 2;
    public const int MaxDots = 3;
    public const double HorizontalBaseHeight = 40;
    public const double VerticalBaseHeight = 46;
    public const double BarLevelHeight = 4;
    public const double MoreHeight = 14;

    public static CalendarDayDensity Build(IEnumerable<ScheduleEvent> events, DateOnly date) =>
        Build(events, Array.Empty<TodoItem>(), date);

    public static CalendarDayDensity Build(
        IEnumerable<ScheduleEvent> events,
        IEnumerable<TodoItem> todos,
        DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(todos);

        var items = events
            .Where(item => item.StartDate <= date && (item.EndDate ?? item.StartDate) >= date)
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .OrderBy(item => item.StartTime ?? TimeOnly.MinValue)
            .ThenBy(item => item.Title, StringComparer.CurrentCulture)
            .ToArray();

        var todoItems = todos
            .Where(item => !item.Done && item.DueDate == date)
            .GroupBy(item => item.Id)
            .Select(group => group.First())
            .ToArray();

        var bars = BuildBars(items, date);
        var total = items.Length + todoItems.Length;
        var crossDay = CountCrossDay(items);
        var hidden = Math.Max(
            0,
            Math.Max(total - MaxDots, crossDay - MaxBars));

        return new CalendarDayDensity(
            total,
            bars,
            total > MaxDots || crossDay > MaxBars,
            hidden);
    }

    /// <summary>基础行高 + 可见色条档数 × 4 + N+ 增量。</summary>
    public static double RowHeight(double baseHeight, CalendarDayDensity density)
    {
        ArgumentNullException.ThrowIfNull(density);

        var levels = density.Bars.Count == 0 ? 0 : density.Bars.Max(bar => bar.Level) + 1;
        return baseHeight + levels * BarLevelHeight + (density.HasMore ? MoreHeight : 0);
    }

    private static IReadOnlyList<CalendarBarModel> BuildBars(
        IReadOnlyList<ScheduleEvent> items,
        DateOnly date)
    {
        var crossDay = items
            .Where(item => (item.EndDate ?? item.StartDate) > item.StartDate)
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.Id)
            .ToArray();

        var bars = new List<CalendarBarModel>(MaxBars);
        var levelEnds = new List<DateOnly>(MaxBars);

        foreach (var item in crossDay)
        {
            var end = item.EndDate ?? item.StartDate;
            var level = levelEnds.FindIndex(occupiedEnd => occupiedEnd < item.StartDate);

            if (level < 0)
            {
                if (levelEnds.Count >= MaxBars)
                {
                    continue;
                }

                level = levelEnds.Count;
                levelEnds.Add(end);
            }
            else
            {
                levelEnds[level] = end;
            }

            bars.Add(new CalendarBarModel(
                item.Title,
                IsStart: date == item.StartDate,
                IsEnd: date == end,
                Level: level,
                Priority: item.Priority));
        }

        return bars;
    }

    private static int CountCrossDay(IEnumerable<ScheduleEvent> items) =>
        items.Count(item => (item.EndDate ?? item.StartDate) > item.StartDate);
}
