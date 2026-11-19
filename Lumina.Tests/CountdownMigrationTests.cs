using Lumina.Data.Countdowns;
using Lumina.Data.Migrations;
using Lumina.Domain.Countdowns;
using Lumina.Domain.Enums;

namespace Lumina.Tests;

/// <summary>
/// 迁移 v6：旧 `date`（现 anchor_date）按 repeat 反推锚点并删除旧日期；
/// 非当前重复方式的锚点一并清空；非法枚举回落默认值。
/// </summary>
public class CountdownMigrationTests
{
    [Fact]
    public void Legacy_countdown_rows_are_migrated_to_anchors()
    {
        using var temp = new TemporaryDataRoot();
        temp.CreateInitializer(MigrationCatalog.All.Where(migration => migration.Version <= 5).ToArray())
            .Initialize(TestContext.Current.CancellationToken);

        InsertLegacyRows(temp);

        var result = temp.CreateInitializer().Initialize(TestContext.Current.CancellationToken);
        Assert.Equal(MigrationCatalog.LatestVersion, result.CurrentVersion);

        var store = new CountdownRepository(temp.Database);
        var items = store.QueryAll().ToDictionary(item => item.LegacyId!);

        var weekly = items["cd_weekly"];
        Assert.Equal(CountdownRepeat.Weekly, weekly.Repeat);
        Assert.Equal(2, weekly.AnchorWeekday); // 2026-11-10 是周二
        Assert.Null(weekly.AnchorDate);
        Assert.Null(weekly.AnchorDay);
        Assert.Null(weekly.AnchorMonth);
        Assert.False(weekly.AnchorMonthEnd);

        var monthly = items["cd_monthly"];
        Assert.Equal(CountdownRepeat.Monthly, monthly.Repeat);
        Assert.Equal(30, monthly.AnchorDay);
        Assert.False(monthly.AnchorMonthEnd);
        Assert.Null(monthly.AnchorDate);
        Assert.Null(monthly.AnchorWeekday);

        var monthEnd = items["cd_month_end"];
        Assert.True(monthEnd.AnchorMonthEnd);
        Assert.Null(monthEnd.AnchorDay);
        Assert.Null(monthEnd.AnchorDate);

        var yearly = items["cd_yearly"];
        Assert.Equal(CountdownRepeat.Yearly, yearly.Repeat);
        Assert.Equal(2, yearly.AnchorMonth);
        Assert.Equal(29, yearly.AnchorDay);
        Assert.Null(yearly.AnchorDate);
        Assert.False(yearly.AnchorMonthEnd);

        var once = items["cd_once"];
        Assert.Equal(new DateOnly(2026, 12, 1), once.AnchorDate);
        Assert.Null(once.AnchorDay);
        Assert.Null(once.AnchorWeekday);
        Assert.Null(once.AnchorMonth);

        var illegal = items["cd_illegal"];
        Assert.Equal(CountdownRepeat.None, illegal.Repeat);
        Assert.Equal(CountdownCategory.Other, illegal.Category);
        Assert.Equal(ReminderOffset.Day1, illegal.Reminder);
    }

    [Fact]
    public void Migrated_weekly_anchor_rolls_from_migration_date()
    {
        using var temp = new TemporaryDataRoot();
        temp.CreateInitializer(MigrationCatalog.All.Where(migration => migration.Version <= 5).ToArray())
            .Initialize(TestContext.Current.CancellationToken);
        InsertLegacyRows(temp);
        temp.CreateInitializer().Initialize(TestContext.Current.CancellationToken);

        var store = new CountdownRepository(temp.Database);
        var weekly = store.QueryAll().Single(item => item.LegacyId == "cd_weekly");

        var next = CountdownCalendar.NextOccurrence(weekly, new DateOnly(2026, 11, 10));
        Assert.Equal(new DateOnly(2026, 11, 10), next);
    }

    private static void InsertLegacyRows(TemporaryDataRoot temp)
    {
        temp.Database.ExecuteInTransaction((connection, transaction) =>
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO countdown_item
                  (legacy_id, name, category, repeat, remind,
                   anchor_date, anchor_weekday, anchor_day, anchor_month, anchor_month_end,
                   created_at, updated_at)
                VALUES
                  ('cd_weekly', '每周复盘', 'work', 'weekly', 'd1',
                   '2026-11-10', NULL, NULL, NULL, 0, '2026-11-01T00:00:00+08:00', '2026-11-01T00:00:00+08:00'),
                  ('cd_monthly', '每月结算', 'life', 'monthly', 'm15',
                   '2026-11-30', NULL, NULL, NULL, 0, '2026-11-01T00:00:00+08:00', '2026-11-01T00:00:00+08:00'),
                  ('cd_month_end', '月底交接', 'work', 'monthly', 'h1',
                   '2026-02-28', NULL, 28, NULL, 1, '2026-11-01T00:00:00+08:00', '2026-11-01T00:00:00+08:00'),
                  ('cd_yearly', '生日', 'anniv', 'yearly', 'd1',
                   '2028-02-29', NULL, 29, 2, 0, '2026-11-01T00:00:00+08:00', '2026-11-01T00:00:00+08:00'),
                  ('cd_once', '提交材料', 'exam', 'none', 'd1',
                   '2026-12-01', 2, 5, 11, 0, '2026-11-01T00:00:00+08:00', '2026-11-01T00:00:00+08:00'),
                  ('cd_illegal', '旧数据', 'unknown', 'daily', 'zzz',
                   NULL, NULL, NULL, NULL, 0, '2026-11-01T00:00:00+08:00', '2026-11-01T00:00:00+08:00');
                """;
            command.ExecuteNonQuery();
        });
    }
}
