using System.Collections.Concurrent;

namespace Lumina.Services.Calendar;

/// <summary>
/// 二十四节气计算：按太阳视黄经（Meeus 低精度公式）求 15° 倍数时刻，
/// 再换算为北京时间日期。结果按年份缓存；1901–2100 范围外返回空表。
/// </summary>
public static class SolarTermCalculator
{
    private static readonly string[] Names =
    [
        "小寒", "大寒", "立春", "雨水", "惊蛰", "春分",
        "清明", "谷雨", "立夏", "小满", "芒种", "夏至",
        "小暑", "大暑", "立秋", "处暑", "白露", "秋分",
        "寒露", "霜降", "立冬", "小雪", "大雪", "冬至",
    ];

    private static readonly double[] Longitudes =
    [
        285, 300, 315, 330, 345, 0,
        15, 30, 45, 60, 75, 90,
        105, 120, 135, 150, 165, 180,
        195, 210, 225, 240, 255, 270,
    ];

    private static readonly ConcurrentDictionary<int, IReadOnlyDictionary<DateOnly, string>> Cache = new();

    public static IReadOnlyDictionary<DateOnly, string> TermsForYear(int year)
    {
        if (year is < 1901 or > 2100)
        {
            return new Dictionary<DateOnly, string>();
        }

        return Cache.GetOrAdd(year, ComputeYear);
    }

    public static string? TermOn(DateOnly date) =>
        TermsForYear(date.Year).TryGetValue(date, out var name) ? name : null;

    private static IReadOnlyDictionary<DateOnly, string> ComputeYear(int year)
    {
        var terms = new Dictionary<DateOnly, string>(Names.Length);

        for (var index = 0; index < Names.Length; index++)
        {
            var estimate = new DateTime(year, 1, 6, 0, 0, 0, DateTimeKind.Utc).AddDays(index * 15.2184);
            var moment = FindCrossing(estimate.AddDays(-12), estimate.AddDays(12), Longitudes[index]);
            if (moment is not { } found)
            {
                // 未找到穿越点：宁可不写，也不要把窗口末端当作节气日期。
                continue;
            }

            var beijingDate = DateOnly.FromDateTime(found.AddHours(8));
            terms[beijingDate] = Names[index];
        }

        return terms;
    }

    private static DateTime? FindCrossing(DateTime start, DateTime end, double target)
    {
        var step = TimeSpan.FromHours(6);
        var previous = start;
        var previousDelta = SignedDelta(SolarLongitude(start), target);

        for (var current = start + step; current <= end; current += step)
        {
            var delta = SignedDelta(SolarLongitude(current), target);

            if (previousDelta <= 0 && delta > 0)
            {
                var low = previous;
                var high = current;

                for (var i = 0; i < 40; i++)
                {
                    var middle = low + TimeSpan.FromTicks((high - low).Ticks / 2);
                    if (SignedDelta(SolarLongitude(middle), target) <= 0)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                return low;
            }

            previous = current;
            previousDelta = delta;
        }

        return null;
    }

    /// <summary>太阳视黄经，单位度，0–360。</summary>
    private static double SolarLongitude(DateTime utc)
    {
        var julianDay = utc.ToOADate() + 2415018.5;
        var t = (julianDay - 2451545.0) / 36525.0;

        var meanLongitude = 280.46646 + 36000.76983 * t + 0.0003032 * t * t;
        var meanAnomaly = 357.52911 + 35999.05029 * t - 0.0001537 * t * t;

        var center = (1.914602 - 0.004817 * t - 0.000014 * t * t) * Sin(meanAnomaly)
            + (0.019993 - 0.000101 * t) * Sin(2 * meanAnomaly)
            + 0.000289 * Sin(3 * meanAnomaly);

        var omega = 125.04 - 1934.136 * t;
        var apparent = meanLongitude + center - 0.00569 - 0.00478 * Sin(omega);

        return Normalize(apparent);
    }

    private static double SignedDelta(double longitude, double target)
    {
        var delta = Normalize(longitude - target);
        return delta > 180 ? delta - 360 : delta;
    }

    private static double Normalize(double degrees)
    {
        var value = degrees % 360;
        return value < 0 ? value + 360 : value;
    }

    private static double Sin(double degrees) => Math.Sin(degrees * Math.PI / 180);
}
