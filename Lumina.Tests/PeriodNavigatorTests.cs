using Lumina.Domain.Enums;
using Lumina.Services.Focus;

namespace Lumina.Tests;

/// <summary>P3-02 周期导航：标签、区间文本、翻页边界。</summary>
public class PeriodNavigatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 10, 13, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void Defaults_to_current_week_and_cannot_go_forward()
    {
        var navigator = new PeriodNavigator();

        Assert.Equal(PeriodKind.Week7, navigator.Kind);
        Assert.False(navigator.CanGoNext);
        Assert.True(navigator.ShowsPager);
        Assert.Equal("本周", navigator.Label(Now));
        Assert.Equal("11-04 ~ 11-10", navigator.RangeText(Now));
        Assert.False(navigator.MoveNext());
    }

    [Fact]
    public void Previous_week_shifts_range_and_label()
    {
        var navigator = new PeriodNavigator();

        Assert.True(navigator.MovePrevious());

        Assert.Equal(-1, navigator.Offset);
        Assert.True(navigator.CanGoNext);
        Assert.Equal("上周", navigator.Label(Now));
        Assert.Equal("10-28 ~ 11-03", navigator.RangeText(Now));

        navigator.MovePrevious();
        Assert.Equal("2 周前", navigator.Label(Now));

        Assert.True(navigator.MoveNext());
        Assert.Equal("上周", navigator.Label(Now));
    }

    [Fact]
    public void Month30_labels_and_range()
    {
        var navigator = new PeriodNavigator();
        navigator.Switch(PeriodKind.Month30);

        Assert.Equal("最近 30 天", navigator.Label(Now));
        Assert.Equal("10-12 ~ 11-10", navigator.RangeText(Now));

        navigator.MovePrevious();
        Assert.Equal("上一个 30 天", navigator.Label(Now));
    }

    [Fact]
    public void Archive_hides_pager_and_labels_by_calendar_month()
    {
        var navigator = new PeriodNavigator();
        navigator.Switch(PeriodKind.MonthlyArchive);

        Assert.False(navigator.ShowsPager);
        Assert.Equal("2026 年 11 月", navigator.Label(Now));
        Assert.Equal("11-01 ~ 11-30", navigator.RangeText(Now));

        navigator.MovePrevious();
        Assert.Equal("2026 年 10 月", navigator.Label(Now));
        Assert.Equal("10-01 ~ 10-31", navigator.RangeText(Now));
    }

    [Fact]
    public void SetOffset_jumps_back_and_never_into_future()
    {
        var navigator = new PeriodNavigator();

        navigator.SetOffset(-3);
        Assert.Equal("3 周前", navigator.Label(Now));

        navigator.SetOffset(5);
        Assert.Equal(0, navigator.Offset);
        Assert.Equal("本周", navigator.Label(Now));
    }

    [Fact]
    public void Switching_period_resets_offset_to_current()
    {
        var navigator = new PeriodNavigator();
        navigator.MovePrevious();
        navigator.MovePrevious();

        navigator.Switch(PeriodKind.Week7);
        Assert.Equal(0, navigator.Offset);

        navigator.MovePrevious();
        navigator.Switch(PeriodKind.Month30);
        Assert.Equal(0, navigator.Offset);
    }
}
