using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lumina.Services.Contracts;

namespace Lumina.Services.Focus;

/// <summary>
/// 统计导出（P3-02）：CSV 使用 UTF-8 BOM + CRLF（Excel 直接打开不乱码），
/// JSON 使用 camelCase 固定字段，便于后续导入与自动化比对。
/// </summary>
public static class FocusStatsExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToCsv(FocusStatsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var current = snapshot.Current;

        var builder = new StringBuilder();
        AppendRow(builder, "统计周期", current.Kind.ToString(), $"{current.Start:yyyy-MM-dd} ~ {current.End:yyyy-MM-dd}");
        AppendRow(builder, "专注分钟", current.FocusMinutes.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "番茄数", current.PomodoroCount.ToString(CultureInfo.InvariantCulture));
        AppendRow(
            builder,
            "待办完成",
            current.CompletedTodos.ToString(CultureInfo.InvariantCulture),
            current.TotalTodos.ToString(CultureInfo.InvariantCulture));
        AppendRow(
            builder,
            "待办完成率",
            current.TotalTodos == 0
                ? "0%"
                : $"{(double)current.CompletedTodos / current.TotalTodos:P1}");
        AppendRow(builder, "今日专注分钟", snapshot.TodayFocusMinutes.ToString(CultureInfo.InvariantCulture));
        AppendRow(builder, "今日目标分钟", snapshot.TodayTargetMinutes.ToString(CultureInfo.InvariantCulture));
        builder.Append("\r\n");

        AppendRow(builder, "日期", "专注分钟", "番茄数", "目标分钟", "是否达成");
        foreach (var point in snapshot.Daily)
        {
            AppendRow(
                builder,
                point.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                point.FocusMinutes.ToString(CultureInfo.InvariantCulture),
                point.PomodoroCount.ToString(CultureInfo.InvariantCulture),
                point.TargetMinutes.ToString(CultureInfo.InvariantCulture),
                point.GoalMet ? "是" : "否");
        }

        return builder.ToString();
    }

    public static string ToJson(FocusStatsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var payload = new
        {
            current = snapshot.Current,
            previous = snapshot.Previous,
            daily = snapshot.Daily,
            today = new
            {
                focusMinutes = snapshot.TodayFocusMinutes,
                targetMinutes = snapshot.TodayTargetMinutes,
            },
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>写 CSV（UTF-8 BOM）；返回写入的完整路径。</summary>
    public static string WriteCsv(string path, FocusStatsSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, ToCsv(snapshot), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return Path.GetFullPath(path);
    }

    /// <summary>写 JSON（UTF-8 无 BOM）；返回写入的完整路径。</summary>
    public static string WriteJson(string path, FocusStatsSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, ToJson(snapshot), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return Path.GetFullPath(path);
    }

    private static void AppendRow(StringBuilder builder, params string[] fields)
    {
        for (var index = 0; index < fields.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(fields[index]));
        }

        builder.Append("\r\n");
    }

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
