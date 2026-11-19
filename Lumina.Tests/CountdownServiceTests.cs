using Lumina.Data.Countdowns;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Countdowns;

namespace Lumina.Tests;

/// <summary>
/// P3-03 倒计时服务：CRUD、名称与锚点归一化、切换重复清空旧锚点、
/// 紧迫度排序与 Changed 通知。
/// </summary>
public class CountdownServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 10, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Add_weekly_derives_weekday_from_legacy_date()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var created = await service.AddOrUpdateAsync(
            new CountdownItem
            {
                Name = "  每周复盘  ",
                Repeat = CountdownRepeat.Weekly,
                AnchorDate = new DateOnly(2026, 11, 10), // 周二
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken);

        Assert.True(created.Id > 0);
        Assert.Equal("每周复盘", created.Name);
        Assert.Equal(2, created.AnchorWeekday);
        Assert.Null(created.AnchorDate);
        Assert.Equal(CountdownCategory.Other, created.Category);
        Assert.Equal(ReminderOffset.Day1, created.Reminder);

        var reloaded = Assert.Single(service.All());
        Assert.Equal(2, reloaded.AnchorWeekday);
        Assert.Equal(0, service.Evaluate(reloaded).DaysRemaining);
    }

    [Fact]
    public async Task Add_month_end_clears_day_number()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var created = await service.AddOrUpdateAsync(
            new CountdownItem
            {
                Name = "月底结算",
                Repeat = CountdownRepeat.Monthly,
                AnchorMonthEnd = true,
                AnchorDay = 15,
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken);

        Assert.True(created.AnchorMonthEnd);
        Assert.Null(created.AnchorDay);
        Assert.Null(created.AnchorWeekday);
        Assert.Null(created.AnchorDate);
        Assert.Equal(new DateOnly(2026, 11, 30), service.Evaluate(created).NextOccurrence);
    }

    [Fact]
    public async Task Update_preserves_created_at_and_replaces_fields()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var created = await service.AddOrUpdateAsync(
            Once("考试", new DateOnly(2026, 12, 20)),
            TestContext.Current.CancellationToken);

        var updated = await service.AddOrUpdateAsync(
            created with
            {
                Name = "考试（改）",
                Category = CountdownCategory.Exam,
                Reminder = ReminderOffset.Minutes30,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal("考试（改）", updated.Name);
        Assert.Equal(CountdownCategory.Exam, updated.Category);
        Assert.Equal(ReminderOffset.Minutes30, updated.Reminder);
        Assert.Single(service.All());
    }

    [Fact]
    public async Task Switching_repeat_keeps_only_current_anchor_set()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var monthly = await service.AddOrUpdateAsync(
            new CountdownItem
            {
                Name = "月度提醒",
                Repeat = CountdownRepeat.Monthly,
                AnchorDay = 10,
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken);

        // 只改重复方式、不重新选月份：旧日号不得被当作年重复的日号复用。
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddOrUpdateAsync(
            monthly with { Repeat = CountdownRepeat.Yearly },
            TestContext.Current.CancellationToken));

        var switched = await service.AddOrUpdateAsync(
            monthly with
            {
                Repeat = CountdownRepeat.Yearly,
                AnchorMonth = 11,
                AnchorDay = 10,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(CountdownRepeat.Yearly, switched.Repeat);
        Assert.Equal(11, switched.AnchorMonth);
        Assert.Equal(10, switched.AnchorDay);
        Assert.Null(switched.AnchorWeekday);
        Assert.False(switched.AnchorMonthEnd);
        Assert.Null(switched.AnchorDate);

        var once = await service.AddOrUpdateAsync(
            switched with { Repeat = CountdownRepeat.None, AnchorDate = new DateOnly(2026, 12, 1) },
            TestContext.Current.CancellationToken);

        Assert.Equal(new DateOnly(2026, 12, 1), once.AnchorDate);
        Assert.Null(once.AnchorMonth);
        Assert.Null(once.AnchorDay);
    }

    [Fact]
    public async Task Add_without_required_anchor_throws()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddOrUpdateAsync(
            Once("没有日期", null),
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddOrUpdateAsync(
            new CountdownItem
            {
                Name = "每周没有星期",
                Repeat = CountdownRepeat.Weekly,
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken));

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddOrUpdateAsync(
            new CountdownItem
            {
                Name = "2 月 30 日",
                Repeat = CountdownRepeat.Yearly,
                AnchorMonth = 2,
                AnchorDay = 30,
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Add_with_blank_name_throws()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddOrUpdateAsync(
            Once("   ", new DateOnly(2026, 12, 1)),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task All_sorts_active_by_days_then_expired_last()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        await service.AddOrUpdateAsync(Once("已过期", new DateOnly(2026, 11, 1)), TestContext.Current.CancellationToken);
        await service.AddOrUpdateAsync(Once("三天后", new DateOnly(2026, 11, 13)), TestContext.Current.CancellationToken);
        await service.AddOrUpdateAsync(
            new CountdownItem
            {
                Name = "每周五",
                Repeat = CountdownRepeat.Weekly,
                AnchorWeekday = 5,
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken);
        await service.AddOrUpdateAsync(
            new CountdownItem
            {
                Name = "每月 31",
                Repeat = CountdownRepeat.Monthly,
                AnchorDay = 31,
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken);
        await service.AddOrUpdateAsync(
            new CountdownItem
            {
                Name = "每年 2/29",
                Repeat = CountdownRepeat.Yearly,
                AnchorMonth = 2,
                AnchorDay = 29,
                CreatedAt = Now,
                UpdatedAt = Now,
            },
            TestContext.Current.CancellationToken);

        var names = service.All().Select(item => item.Name).ToArray();

        Assert.Equal(new[] { "三天后", "每周五", "每月 31", "每年 2/29", "已过期" }, names);
    }

    [Fact]
    public async Task Delete_removes_items_and_raises_changed()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);
        var changes = 0;
        service.Changed += () => changes++;

        var first = await service.AddOrUpdateAsync(
            Once("第一条", new DateOnly(2026, 12, 1)),
            TestContext.Current.CancellationToken);
        var second = await service.AddOrUpdateAsync(
            Once("第二条", new DateOnly(2026, 12, 2)),
            TestContext.Current.CancellationToken);

        await service.DeleteAsync(new[] { first.Id }, TestContext.Current.CancellationToken);

        var remaining = Assert.Single(service.All());
        Assert.Equal(second.Id, remaining.Id);
        Assert.Equal(3, changes);
    }

    [Fact]
    public async Task Invalid_enum_values_fall_back_to_defaults()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var created = await service.AddOrUpdateAsync(
            Once("枚举兜底", new DateOnly(2026, 12, 1)) with
            {
                Category = (CountdownCategory)99,
                Reminder = (ReminderOffset)99,
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(CountdownCategory.Other, created.Category);
        Assert.Equal(ReminderOffset.Day1, created.Reminder);
    }

    [Fact]
    public async Task Evaluate_uses_injected_local_today()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);
        var created = await service.AddOrUpdateAsync(
            Once("跨天", new DateOnly(2026, 11, 11)),
            TestContext.Current.CancellationToken);

        var snapshot = service.Evaluate(created);

        Assert.Equal(1, snapshot.DaysRemaining);
        Assert.Equal(UrgencyLevel.Urgent, snapshot.Level);
        Assert.Equal(new DateOnly(2026, 11, 11), snapshot.NextOccurrence);
        Assert.True(snapshot.ShouldBreathe);
    }

    private static CountdownItem Once(string name, DateOnly? date) =>
        new()
        {
            Name = name,
            Repeat = CountdownRepeat.None,
            AnchorDate = date,
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private static CountdownService CreateService(TemporaryDataRoot temp) =>
        new(new CountdownRepository(temp.Database), () => Now);
}
