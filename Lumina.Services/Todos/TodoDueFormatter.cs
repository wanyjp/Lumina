using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Contracts;

namespace Lumina.Services.Todos;

/// <summary>待办截止日期文案：相对日期与自然日延期天数。</summary>
public static class TodoDueFormatter
{
    public static string Format(TodoItem item, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.DueDate is not { } dueDate)
        {
            return "无截止";
        }

        var relative = Relative(dueDate, today);
        var time = item.DueTime is { } dueTime ? $" {dueTime:HH:mm}" : string.Empty;

        if (!item.Done && dueDate < today)
        {
            var overdueDays = today.DayNumber - dueDate.DayNumber;
            return $"{relative}{time} | 延期 {overdueDays} 天";
        }

        return $"{relative}{time}";
    }

    public static string Relative(DateOnly date, DateOnly today)
    {
        var delta = date.DayNumber - today.DayNumber;

        return delta switch
        {
            0 => "今天",
            1 => "明天",
            -1 => "昨天",
            _ => $"{date.Month} 月 {date.Day} 日",
        };
    }
}
