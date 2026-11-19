using System.Globalization;

namespace Lumina.Services.Clock;

/// <summary>
/// 世界时钟时区解析：IANA/Windows 时区走系统库；<c>UTC+08:00</c> 这类固定偏移
/// 用自定义时区表示，便于用户按“东八区/东六区”选择（例如新疆、西藏的实际时差）。
/// </summary>
public static class WorldClockTimeZoneResolver
{
    /// <summary>解析时区 ID；固定偏移（UTC±HH:mm）不依赖系统时区库。</summary>
    public static TimeZoneInfo Resolve(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return TryParseFixedOffset(id, out var offset)
            ? TimeZoneInfo.CreateCustomTimeZone(id, offset, id, id)
            : TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    public static bool TryResolve(string id, out TimeZoneInfo? zone)
    {
        try
        {
            zone = Resolve(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException
                                       or InvalidTimeZoneException
                                       or ArgumentException)
        {
            zone = null;
            return false;
        }
    }

    /// <summary>“UTC+8”“UTC+05:30”“GMT-3”等固定偏移。</summary>
    public static bool TryParseFixedOffset(string id, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var text = id.Trim();
        if (!text.StartsWith("UTC", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("GMT", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        text = text[3..].Trim();
        if (text.Length < 2 || (text[0] != '+' && text[0] != '-'))
        {
            return false;
        }

        var negative = text[0] == '-';
        var parts = text[1..].Split(':');
        if (parts.Length is < 1 or > 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)
            || hours is < 0 or > 14)
        {
            return false;
        }

        var minutes = 0;
        if (parts.Length == 2
            && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes)
                || minutes is < 0 or > 59))
        {
            return false;
        }

        offset = new TimeSpan(hours, minutes, 0);
        if (negative)
        {
            offset = offset.Negate();
        }

        return true;
    }

    /// <summary>偏移的展示文本：UTC+8 / UTC+5:30 / UTC-3。</summary>
    public static string DescribeOffset(TimeZoneInfo zone, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var offset = zone.GetUtcOffset(now);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return abs.Minutes == 0
            ? $"UTC{sign}{abs.Hours}"
            : $"UTC{sign}{abs.Hours}:{abs.Minutes.ToString("00", CultureInfo.InvariantCulture)}";
    }
}
