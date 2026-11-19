using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Calendar;
using Lumina.Services.Contracts;

namespace Lumina.Tests;

/// <summary>P1-08 月历密度：色条、N+、去重与行高增量。</summary>
public class CalendarDensityBuilderTests
{
    private static readonly DateOnly Day = new(2026, 11, 6);

    [Fact]
    public void Three_items_show_no_more_indicator()
    {
        var events = new[]
        {
            Event(1, "九点", new TimeOnly(9, 0)),
            Event(2, "十点", new TimeOnly(10, 0)),
            Event(3, "全天"),
        };

        var density = CalendarDensityBuilder.Build(events, Day);

        Assert.Equal(3, density.ItemCount);
        Assert.False(density.HasMore);
        Assert.Equal(0, density.HiddenCount);
        Assert.Empty(density.Bars);
    }

    [Fact]
    public void Four_items_show_one_hidden()
    {
        var events = new[]
        {
            Event(1, "九点", new TimeOnly(9, 0)),
            Event(2, "十点", new TimeOnly(10, 0)),
            Event(3, "十一点", new TimeOnly(11, 0)),
            Event(4, "全天"),
        };

        var density = CalendarDensityBuilder.Build(events, Day);

        Assert.Equal(4, density.ItemCount);
        Assert.True(density.HasMore);
        Assert.Equal(1, density.HiddenCount);
    }

    [Fact]
    public void Cross_day_bars_have_start_end_and_levels()
    {
        var first = new ScheduleEvent
        {
            Id = 10,
            Title = "出差",
            StartDate = new DateOnly(2026, 11, 4),
            EndDate = new DateOnly(2026, 11, 7),
            Priority = Priority.High,
        };
        var second = new ScheduleEvent
        {
            Id = 11,
            Title = "培训",
            StartDate = new DateOnly(2026, 11, 5),
            EndDate = new DateOnly(2026, 11, 6),
            Priority = Priority.Normal,
        };

        var start = CalendarDensityBuilder.Build(new[] { first }, new DateOnly(2026, 11, 4));
        Assert.Single(start.Bars);
        Assert.True(start.Bars[0].IsStart);
        Assert.False(start.Bars[0].IsEnd);
        Assert.Equal(0, start.Bars[0].Level);

        var middle = CalendarDensityBuilder.Build(new[] { first, second }, new DateOnly(2026, 11, 5));
        Assert.Equal(2, middle.Bars.Count);
        Assert.Equal(0, middle.Bars[0].Level);
        Assert.Equal(1, middle.Bars[1].Level);
        Assert.True(middle.Bars[1].IsStart);

        var end = CalendarDensityBuilder.Build(new[] { first }, new DateOnly(2026, 11, 7));
        Assert.True(end.Bars[0].IsEnd);
    }

    [Fact]
    public void More_than_two_bars_are_truncated_and_counted()
    {
        var events = new[]
        {
            CrossDay(1, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 7)),
            CrossDay(2, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 6)),
            CrossDay(3, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 8)),
        };

        var density = CalendarDensityBuilder.Build(events, new DateOnly(2026, 11, 5));

        Assert.Equal(2, density.Bars.Count);
        Assert.True(density.HasMore);
        Assert.Equal(1, density.HiddenCount);
    }

    [Fact]
    public void Same_series_on_same_day_is_counted_once()
    {
        var duplicate = Event(7, "每日站会", new TimeOnly(9, 30));
        var density = CalendarDensityBuilder.Build(new[] { duplicate, duplicate }, Day);

        Assert.Equal(1, density.ItemCount);
    }

    [Fact]
    public void Row_height_adds_bar_and_more_increments()
    {
        var withTwoBarsAndMore = new CalendarDayDensity(
            5,
            new[]
            {
                new CalendarBarModel("A", true, false, 0),
                new CalendarBarModel("B", false, false, 1),
            },
            HasMore: true,
            HiddenCount: 2);

        var base40 = CalendarDensityBuilder.RowHeight(
            CalendarDensityBuilder.HorizontalBaseHeight,
            withTwoBarsAndMore);
        var base46 = CalendarDensityBuilder.RowHeight(
            CalendarDensityBuilder.VerticalBaseHeight,
            withTwoBarsAndMore);

        Assert.Equal(40 + 8 + 14, base40);
        Assert.Equal(46 + 8 + 14, base46);

        var empty = new CalendarDayDensity(0, Array.Empty<CalendarBarModel>(), false, 0);
        Assert.Equal(40, CalendarDensityBuilder.RowHeight(40, empty));
    }

    [Fact]
    public void Todo_items_join_item_count_and_more_indicator()
    {
        var events = new[]
        {
            Event(1, "九点", new TimeOnly(9, 0)),
            Event(2, "十点", new TimeOnly(10, 0)),
        };
        var todos = new[]
        {
            Todo(101, done: false),
            Todo(102, done: false),
            Todo(103, done: false),
            Todo(104, done: true),
        };

        var density = CalendarDensityBuilder.Build(events, todos, Day);

        Assert.Equal(5, density.ItemCount);
        Assert.True(density.HasMore);
        Assert.Equal(2, density.HiddenCount);
    }

    private static TodoItem Todo(long id, bool done) =>
        new()
        {
            Id = id,
            Text = $"待办 {id}",
            DueDate = Day,
            Done = done,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };

    private static ScheduleEvent Event(long id, string title, TimeOnly? startTime = null) =>
        new()
        {
            Id = id,
            Title = title,
            StartDate = Day,
            StartTime = startTime,
        };

    private static ScheduleEvent CrossDay(long id, DateOnly start, DateOnly end) =>
        new()
        {
            Id = id,
            Title = $"跨天 {id}",
            StartDate = start,
            EndDate = end,
        };
}
