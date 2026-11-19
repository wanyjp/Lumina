using System.Globalization;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;

namespace Lumina.Services.Reminders;

/// <summary>
/// 提醒时间计算：日程按实例开始日期/时间，倒计时按滚动后的下次发生日；
/// 无时间的日期型目标统一锚定在本地 09:00，避免午夜触发错过 5 分钟窗口。
/// </summary>
public static class ReminderPlanner
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public const int AllDayAnchorHour = 9;

    public static DateTimeOffset LocalAt(DateOnly date, TimeOnly? time)
    {
        var local = date.ToDateTime(
            time ?? new TimeOnly(AllDayAnchorHour, 0),
            DateTimeKind.Unspecified);
        var offset = TimeZoneInfo.Local.GetUtcOffset(local);

        try
        {
            return new DateTimeOffset(local, offset);
        }
        catch (ArgumentException)
        {
            // 夏令时跳变的不存在时刻：顺延一小时取有效偏移。
            return new DateTimeOffset(local.AddHours(1), TimeZoneInfo.Local.GetUtcOffset(local.AddHours(1)));
        }
    }

    public static DateTimeOffset ScheduleTarget(ScheduleEvent item) =>
        LocalAt(item.StartDate, item.StartTime);

    public static DateTimeOffset CountdownTarget(DateOnly date) => LocalAt(date, null);

    public static DateTimeOffset TriggerAt(DateTimeOffset target, ReminderOffset offset) =>
        target - offset.ToLeadTime();

    public static bool InWindow(DateTimeOffset triggerAt, DateTimeOffset now) =>
        now >= triggerAt && now - triggerAt < Window;

    public static bool IsMissed(DateTimeOffset triggerAt, DateTimeOffset now) =>
        now - triggerAt >= Window;

    public static string ScheduleKey(ScheduleEvent item, ReminderOffset offset) =>
        $"sched:{item.Id}:{item.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{offset.ToKey()}";

    public static string CountdownKey(CountdownItem item, DateOnly occurrence, ReminderOffset offset) =>
        $"countdown:{item.Id}:{occurrence.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{offset.ToKey()}";

    public static string OffsetLabel(ReminderOffset offset) => offset switch
    {
        ReminderOffset.OnTime => "准时",
        ReminderOffset.Minutes5 => "提前 5 分钟",
        ReminderOffset.Minutes15 => "提前 15 分钟",
        ReminderOffset.Minutes30 => "提前 30 分钟",
        ReminderOffset.Hour1 => "提前 1 小时",
        ReminderOffset.Day1 => "提前 1 天",
        _ => "不提醒",
    };

    /// <summary>日程提醒正文：日期、时间（全天）与地点。</summary>
    public static string ScheduleBody(ScheduleEvent item)
    {
        var time = item.StartTime is { } start
            ? start.ToString("HH:mm", CultureInfo.InvariantCulture)
            : "全天";
        var body = $"{item.StartDate.Month} 月 {item.StartDate.Day} 日 · {time}";

        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            body += $" · {item.Location}";
        }

        return body;
    }

    public static string RouteId(ReminderRoute route) => route switch
    {
        ReminderRoute.Countdown => "countdown",
        _ => "schedule",
    };
}
