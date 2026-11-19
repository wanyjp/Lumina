using Lumina.Domain.Countdowns;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Countdowns;

namespace Lumina.Tests;

/// <summary>P3-04 倒计时文案：分类、重复语义、卡片主文案、剩余与紧迫度说明。</summary>
public class CountdownTextTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    [Theory]
    [InlineData(CountdownCategory.Work, "工作")]
    [InlineData(CountdownCategory.Life, "生活")]
    [InlineData(CountdownCategory.Exam, "考试")]
    [InlineData(CountdownCategory.Anniversary, "纪念日")]
    [InlineData(CountdownCategory.Other, "其他")]
    public void Category_labels_are_stable(CountdownCategory category, string expected)
    {
        Assert.Equal(expected, CountdownText.CategoryLabel(category));
    }

    [Fact]
    public void Repeat_semantic_covers_four_repeat_modes()
    {
        Assert.Equal("不重复", CountdownText.RepeatSemantic(Item(CountdownRepeat.None, date: Today)));
        Assert.Equal("每周二", CountdownText.RepeatSemantic(Item(CountdownRepeat.Weekly, weekday: 2)));
        Assert.Equal("每月 26 号", CountdownText.RepeatSemantic(Item(CountdownRepeat.Monthly, day: 26)));
        Assert.Equal("每月月底", CountdownText.RepeatSemantic(Item(CountdownRepeat.Monthly, monthEnd: true)));
        Assert.Equal("每年 11 月 26 日", CountdownText.RepeatSemantic(Item(CountdownRepeat.Yearly, month: 11, day: 26)));
    }

    [Fact]
    public void Label_and_remaining_reflect_state()
    {
        var future = Snapshot(Item(CountdownRepeat.None, date: Today.AddDays(5)), Today);
        Assert.Equal("距离考试还有", CountdownText.Label(Item(CountdownRepeat.None, date: Today.AddDays(5)), future));
        Assert.Equal("5", CountdownText.Number(future));
        Assert.Equal("还有 5 天", CountdownText.Remaining(future));

        var today = Snapshot(Item(CountdownRepeat.None, name: "生日", date: Today), Today);
        Assert.Equal("今天就是生日", CountdownText.Label(Item(CountdownRepeat.None, name: "生日", date: Today), today));
        Assert.Equal("就是今天", CountdownText.Remaining(today));

        var expiredItem = Item(CountdownRepeat.None, name: "提交材料", date: Today.AddDays(-9));
        var expired = Snapshot(expiredItem, Today);
        Assert.Equal("提交材料已过", CountdownText.Label(expiredItem, expired));
        Assert.Equal("9", CountdownText.Number(expired));
        Assert.Equal("已过去 9 天", CountdownText.Remaining(expired));
    }

    [Fact]
    public void Weekly_label_says_next_and_deadline_name_reads_remaining()
    {
        var weeklyItem = Item(CountdownRepeat.Weekly, name: "周会", weekday: 5);
        var weekly = Snapshot(weeklyItem, Today);
        Assert.Equal("距离下次周会还有", CountdownText.Label(weeklyItem, weekly));

        var deadline = Item(CountdownRepeat.None, name: "项目截止", date: Today.AddDays(12));
        Assert.Equal("项目截止剩余", CountdownText.Label(deadline, Snapshot(deadline, Today)));
    }

    [Fact]
    public void Label_parts_keep_name_separate_for_emphasis()
    {
        var todayItem = Item(CountdownRepeat.None, name: "1", date: Today);
        var today = Snapshot(todayItem, Today);
        Assert.Equal(("今天就是", "1", string.Empty), CountdownText.LabelParts(todayItem, today));
        Assert.Equal("今天就是1", CountdownText.Label(todayItem, today));

        var futureItem = Item(CountdownRepeat.None, name: "2", date: Today.AddDays(1));
        var future = Snapshot(futureItem, Today);
        Assert.Equal(("距离", "2", "还有"), CountdownText.LabelParts(futureItem, future));

        var weeklyItem = Item(CountdownRepeat.Weekly, name: "周会", weekday: 5);
        Assert.Equal(("距离下次", "周会", "还有"), CountdownText.LabelParts(weeklyItem, Snapshot(weeklyItem, Today)));

        var deadline = Item(CountdownRepeat.None, name: "项目截止", date: Today.AddDays(12));
        Assert.Equal((string.Empty, "项目截止", "剩余"), CountdownText.LabelParts(deadline, Snapshot(deadline, Today)));

        var expiredItem = Item(CountdownRepeat.None, name: "提交材料", date: Today.AddDays(-9));
        Assert.Equal((string.Empty, "提交材料", "已过"), CountdownText.LabelParts(expiredItem, Snapshot(expiredItem, Today)));

        var invalid = Item(CountdownRepeat.Yearly, month: 2, day: 30);
        Assert.Equal(("日期无效", string.Empty, string.Empty), CountdownText.LabelParts(invalid, Snapshot(invalid, Today)));
    }

    [Fact]
    public void Invalid_date_has_explicit_text_without_color_dependency()
    {
        var item = Item(CountdownRepeat.Yearly, month: 2, day: 30);
        var snapshot = Snapshot(item, Today);

        Assert.Null(snapshot.NextOccurrence);
        Assert.Equal("日期无效", CountdownText.Label(item, snapshot));
        Assert.Equal("—", CountdownText.Number(snapshot));
        Assert.Equal("—", CountdownText.Remaining(snapshot));
        Assert.Equal("日期无效，请重新选择目标日期", CountdownText.Semantic(item, snapshot));
    }

    [Fact]
    public void Semantic_includes_repeat_rule_and_next_date()
    {
        var weekly = Item(CountdownRepeat.Weekly, weekday: 2);
        Assert.Equal(
            "每周二 · 下次 2026 年 11 月 10 日",
            CountdownText.Semantic(weekly, Snapshot(weekly, Today)));

        var once = Item(CountdownRepeat.None, date: new DateOnly(2026, 12, 1));
        Assert.Equal("2026 年 12 月 1 日", CountdownText.Semantic(once, Snapshot(once, Today)));
        Assert.Equal("—", CountdownText.DateText(null));
    }

    [Fact]
    public void Urgency_notes_are_distinct_for_all_levels()
    {
        var notes = Enum.GetValues<UrgencyLevel>()
            .Select(CountdownText.UrgencyNote)
            .ToArray();

        Assert.Equal(notes.Length, notes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(notes, note => Assert.False(string.IsNullOrWhiteSpace(note)));
    }

    private static UrgencySnapshot Snapshot(CountdownItem item, DateOnly today) =>
        CountdownCalendar.Evaluate(item, today);

    private static CountdownItem Item(
        CountdownRepeat repeat,
        string name = "考试",
        int? weekday = null,
        int? day = null,
        int? month = null,
        bool monthEnd = false,
        DateOnly? date = null) =>
        new()
        {
            Id = 1,
            Name = name,
            Repeat = repeat,
            Reminder = ReminderOffset.Day1,
            AnchorWeekday = weekday,
            AnchorDay = day,
            AnchorMonth = month,
            AnchorMonthEnd = monthEnd,
            AnchorDate = date,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };
}
