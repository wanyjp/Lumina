using System.Globalization;

namespace Lumina.Data.Sqlite;

/// <summary>
/// 仓储层的 SQLite 存储约定：时间戳统一按 UTC 的 ISO-8601（"O"）写入，
/// 使字符串比较与时间顺序一致；读取一律容错，坏记录不会让整表查询失败。
/// </summary>
public static class SqliteConventions
{
    /// <summary>写入用时间戳：转 UTC 后保留 7 位小数与 +00:00 偏移。</summary>
    public static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static string? FormatTimestamp(DateTimeOffset? value) =>
        value is { } present ? FormatTimestamp(present) : null;

    /// <summary>
    /// 容错解析：兼容历史本地偏移格式；空值与损坏值回落到 <paramref name="fallback"/>，
    /// 避免单条坏记录让列表查询整体抛异常。
    /// </summary>
    public static DateTimeOffset ParseTimestamp(
        string? value,
        DateTimeOffset? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed;
        }

        return fallback ?? DateTimeOffset.UnixEpoch;
    }

    /// <summary>容错解析日期（yyyy-MM-dd），非法值回落默认日期。</summary>
    public static DateOnly ParseDate(string? value, DateOnly fallback = default)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && DateOnly.TryParseExact(
                value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return parsed;
        }

        return fallback;
    }
}
