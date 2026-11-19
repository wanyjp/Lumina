using System.Text;
using System.Text.Json;
using Lumina.Domain.Enums;
using Lumina.Services.Contracts;
using Lumina.Services.Focus;

namespace Lumina.Tests;

/// <summary>P3-02 导出：CSV 编码/转义与 JSON 字段稳定性。</summary>
public class FocusStatsExporterTests
{
    private static readonly FocusStatsSnapshot Snapshot = new(
        new PeriodAggregate(PeriodKind.Week7, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 10), 55, 1, 3, 5),
        new PeriodAggregate(PeriodKind.Week7, new DateOnly(2026, 10, 28), new DateOnly(2026, 11, 3), 30, 0, 1, 4),
        [
            new FocusDailyPoint(new DateOnly(2026, 11, 4), 0, 0, 0, false),
            new FocusDailyPoint(new DateOnly(2026, 11, 10), 55, 1, 60, false),
        ],
        TodayFocusMinutes: 55,
        TodayTargetMinutes: 60);

    [Fact]
    public void Csv_has_summary_then_daily_table_with_crlf()
    {
        var csv = FocusStatsExporter.ToCsv(Snapshot);

        Assert.Contains("统计周期,Week7,2026-11-04 ~ 2026-11-10", csv, StringComparison.Ordinal);
        Assert.Contains("专注分钟,55", csv, StringComparison.Ordinal);
        Assert.Contains("待办完成,3,5", csv, StringComparison.Ordinal);
        Assert.Contains("日期,专注分钟,番茄数,目标分钟,是否达成", csv, StringComparison.Ordinal);
        Assert.Contains("2026-11-10,55,1,60,否", csv, StringComparison.Ordinal);
        Assert.Contains("\r\n", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteCsv_emits_utf8_bom_for_excel()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var path = Path.Combine(temp.Root, "focus.csv");

        FocusStatsExporter.WriteCsv(path, Snapshot);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Contains("2026-11-04", File.ReadAllText(path, Encoding.UTF8), StringComparison.Ordinal);
    }

    [Fact]
    public void Json_uses_stable_camel_case_fields()
    {
        var json = FocusStatsExporter.ToJson(Snapshot);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("Week7", root.GetProperty("current").GetProperty("kind").GetString());
        Assert.Equal(55, root.GetProperty("current").GetProperty("focusMinutes").GetInt32());
        Assert.Equal(2, root.GetProperty("daily").GetArrayLength());
        Assert.Equal(30, root.GetProperty("previous").GetProperty("focusMinutes").GetInt32());
        Assert.Equal(55, root.GetProperty("today").GetProperty("focusMinutes").GetInt32());
    }

    [Fact]
    public void Json_writes_without_bom()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var path = Path.Combine(temp.Root, "focus.json");

        FocusStatsExporter.WriteJson(path, Snapshot);

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal((byte)'{', bytes[0]);
    }
}
