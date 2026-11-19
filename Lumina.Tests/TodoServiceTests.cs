using Lumina.Data.Notes;
using Lumina.Data.Settings;
using Lumina.Data.Todos;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Contracts;
using Lumina.Services.Notes;
using Lumina.Services.Todos;

namespace Lumina.Tests;

/// <summary>P1-09 待办：CRUD、完成态、筛选排序、逾期与完成率。</summary>
public class TodoServiceTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static readonly DateTimeOffset Now =
        new(2026, 11, 6, 10, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Add_update_delete_round_trip()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var created = await service.AddAsync(
            NewTodo("  写周报  ", Today, Priority.High, note: "  周五前  "),
            TestContext.Current.CancellationToken);

        Assert.True(created.Id > 0);
        Assert.Equal("写周报", created.Text);
        Assert.Equal("周五前", created.Note);
        Assert.False(created.Done);

        await service.UpdateAsync(
            created with { Text = "写月报", Priority = Priority.Low },
            TestContext.Current.CancellationToken);

        var items = await service.ListAsync(null, TestContext.Current.CancellationToken);
        Assert.Single(items);
        Assert.Equal("写月报", items[0].Text);
        Assert.Equal(Priority.Low, items[0].Priority);

        await service.DeleteAsync(new[] { created.Id }, TestContext.Current.CancellationToken);
        Assert.Empty(await service.ListAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Edit_preserves_done_state_and_time()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var item = await service.AddAsync(
            NewTodo("整理资料", Today),
            TestContext.Current.CancellationToken);
        await service.ToggleDoneAsync(item.Id, TestContext.Current.CancellationToken);

        var before = (await service.ListAsync(null, TestContext.Current.CancellationToken)).Single();
        Assert.True(before.Done);
        Assert.NotNull(before.DoneAt);

        await service.UpdateAsync(
            before with { Text = "整理资料（改）", Done = false, DoneAt = null },
            TestContext.Current.CancellationToken);

        var after = (await service.ListAsync(null, TestContext.Current.CancellationToken)).Single();
        Assert.Equal("整理资料（改）", after.Text);
        Assert.True(after.Done);
        Assert.Equal(before.DoneAt, after.DoneAt);
    }

    [Fact]
    public async Task Toggle_done_sets_and_clears_done_at()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var item = await service.AddAsync(
            NewTodo("倒垃圾", Today),
            TestContext.Current.CancellationToken);

        await service.ToggleDoneAsync(item.Id, TestContext.Current.CancellationToken);
        var done = (await service.ListAsync(null, TestContext.Current.CancellationToken)).Single();
        Assert.True(done.Done);
        Assert.Equal(Now, done.DoneAt);

        await service.ToggleDoneAsync(item.Id, TestContext.Current.CancellationToken);
        var reopened = (await service.ListAsync(null, TestContext.Current.CancellationToken)).Single();
        Assert.False(reopened.Done);
        Assert.Null(reopened.DoneAt);
    }

    [Fact]
    public async Task Filters_apply_status_priority_and_date_ranges()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var overdue = await service.AddAsync(
            NewTodo("逾期", Today.AddDays(-1), Priority.High),
            TestContext.Current.CancellationToken);
        var today = await service.AddAsync(
            NewTodo("今天", Today, Priority.Normal),
            TestContext.Current.CancellationToken);
        var noDue = await service.AddAsync(
            NewTodo("无截止", null, Priority.Low),
            TestContext.Current.CancellationToken);
        await service.AddAsync(
            NewTodo("已完成", Today, Priority.Normal),
            TestContext.Current.CancellationToken);
        var done = (await service.ListAsync(null, TestContext.Current.CancellationToken))
            .Single(item => item.Text == "已完成");
        await service.ToggleDoneAsync(done.Id, TestContext.Current.CancellationToken);

        var active = await service.ListAsync(
            new TodoFilter { Status = TodoStatus.Active },
            TestContext.Current.CancellationToken);
        Assert.Equal(3, active.Count);

        var completed = await service.ListAsync(
            new TodoFilter { Status = TodoStatus.Completed },
            TestContext.Current.CancellationToken);
        Assert.Single(completed);

        var overdueOnly = await service.ListAsync(
            new TodoFilter { Status = TodoStatus.Overdue },
            TestContext.Current.CancellationToken);
        Assert.Single(overdueOnly);
        Assert.Equal(overdue.Id, overdueOnly[0].Id);

        var high = await service.ListAsync(
            new TodoFilter { Priority = Priority.High },
            TestContext.Current.CancellationToken);
        Assert.Single(high);

        var dueToday = await service.ListAsync(
            new TodoFilter { DateRange = TodoDateRange.Today },
            TestContext.Current.CancellationToken);
        Assert.Equal(2, dueToday.Count);

        var withoutDue = await service.ListAsync(
            new TodoFilter { DateRange = TodoDateRange.NoDueDate },
            TestContext.Current.CancellationToken);
        Assert.Single(withoutDue);

        Assert.Equal(today.Id, active.Single(item => item.Text == "今天").Id);
        Assert.Equal(noDue.Id, withoutDue[0].Id);
    }

    [Fact]
    public async Task Four_sort_modes_put_completed_items_last()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        await service.AddAsync(NewTodo("低-明天", Today.AddDays(1), Priority.Low),
            TestContext.Current.CancellationToken);
        await service.AddAsync(NewTodo("高-今天", Today, Priority.High),
            TestContext.Current.CancellationToken);
        var done = await service.AddAsync(NewTodo("已完成", Today, Priority.High),
            TestContext.Current.CancellationToken);
        await service.ToggleDoneAsync(done.Id, TestContext.Current.CancellationToken);

        foreach (var sort in new[] { TodoSort.Smart, TodoSort.DueDate, TodoSort.Priority, TodoSort.CreatedAt })
        {
            var items = await service.ListAsync(new TodoFilter { Sort = sort },
                TestContext.Current.CancellationToken);
            Assert.Equal("已完成", items[^1].Text);
        }

        var byDue = await service.ListAsync(new TodoFilter { Sort = TodoSort.DueDate },
            TestContext.Current.CancellationToken);
        Assert.Equal("高-今天", byDue[0].Text);

        var byPriority = await service.ListAsync(new TodoFilter { Sort = TodoSort.Priority },
            TestContext.Current.CancellationToken);
        Assert.Equal("高-今天", byPriority[0].Text);
    }

    [Fact]
    public void Due_formatter_uses_relative_date_and_natural_days()
    {
        Assert.Equal("今天", TodoDueFormatter.Format(NewTodo("A", Today), Today));
        Assert.Equal("明天", TodoDueFormatter.Format(NewTodo("B", Today.AddDays(1)), Today));
        Assert.Equal("昨天 | 延期 1 天", TodoDueFormatter.Format(NewTodo("C", Today.AddDays(-1)), Today));
        Assert.Equal("无截止", TodoDueFormatter.Format(NewTodo("D", null), Today));
        Assert.Equal(
            "12 月 31 日 | 延期 5 天",
            TodoDueFormatter.Format(
                NewTodo("E", new DateOnly(2025, 12, 31)),
                new DateOnly(2026, 1, 5)));
    }

    [Fact]
    public async Task Completion_stats_counts_done_over_total()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        var first = await service.AddAsync(NewTodo("一", Today), TestContext.Current.CancellationToken);
        await service.AddAsync(NewTodo("二", Today), TestContext.Current.CancellationToken);
        await service.ToggleDoneAsync(first.Id, TestContext.Current.CancellationToken);

        var stats = await service.CompletionStatsAsync(
            DateOnly.MinValue,
            DateOnly.MaxValue,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, stats.Total);
        Assert.Equal(1, stats.Completed);
        Assert.Equal(0.5, stats.CompletionRate);
    }

    [Fact]
    public async Task Todos_between_range_returns_due_items_only()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        await service.AddAsync(NewTodo("区间内", Today), TestContext.Current.CancellationToken);
        await service.AddAsync(NewTodo("区间外", Today.AddDays(10)), TestContext.Current.CancellationToken);

        var items = await service.TodosBetweenAsync(
            Today,
            Today.AddDays(1),
            TestContext.Current.CancellationToken);

        Assert.Single(items);
        Assert.Equal("区间内", items[0].Text);
    }

    [Fact]
    public async Task Add_validates_text_and_time_without_due_date()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            NewTodo("  ", null),
            TestContext.Current.CancellationToken));

        var withTime = NewTodo("只有时间", null) with
        {
            DueTime = new TimeOnly(9, 0),
        };
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(
            withTime,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Convert_from_note_creates_todo_with_back_link()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var notes = new NoteService(new NoteRepository(temp.Database), () => Now);
        var note = await notes.SaveAsync(
            new NoteItem { Title = "周会要点", Content = "补测试", CreatedAt = Now, UpdatedAt = Now },
            TestContext.Current.CancellationToken);
        var service = CreateService(temp);

        var todo = await service.ConvertFromNoteAsync(
            note.Id,
            TestContext.Current.CancellationToken);

        Assert.True(todo.Id > 0);
        Assert.Equal("周会要点", todo.Text);
        Assert.Equal(note.Id, todo.NoteId);
    }

    [Fact]
    public async Task Convert_from_note_is_idempotent()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var notes = new NoteService(new NoteRepository(temp.Database), () => Now);
        var note = await notes.SaveAsync(
            new NoteItem { Title = "只转一次", Content = string.Empty, CreatedAt = Now, UpdatedAt = Now },
            TestContext.Current.CancellationToken);
        var service = CreateService(temp);

        var first = await service.ConvertFromNoteAsync(note.Id, TestContext.Current.CancellationToken);
        var second = await service.ConvertFromNoteAsync(note.Id, TestContext.Current.CancellationToken);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await service.ListAsync(null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Convert_untitled_note_uses_first_checklist_line()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var notes = new NoteService(new NoteRepository(temp.Database), () => Now);
        var note = await notes.SaveAsync(
            new NoteItem { Content = "☐ 买菜\n☑ 做饭", CreatedAt = Now, UpdatedAt = Now },
            TestContext.Current.CancellationToken);
        var service = CreateService(temp);

        var todo = await service.ConvertFromNoteAsync(note.Id, TestContext.Current.CancellationToken);

        Assert.Equal("买菜", todo.Text);
    }

    [Fact]
    public async Task Convert_missing_note_throws()
    {
        using var temp = new TemporaryDataRoot();
        temp.Initialize();
        var service = CreateService(temp);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ConvertFromNoteAsync(999, TestContext.Current.CancellationToken));
    }

    private static TodoService CreateService(TemporaryDataRoot temp) =>
        new(new TodoRepository(temp.Database), new NoteRepository(temp.Database), () => Now);

    private static TodoItem NewTodo(
        string text,
        DateOnly? dueDate,
        Priority priority = Priority.Normal,
        string note = "") =>
        new()
        {
            Text = text,
            DueDate = dueDate,
            Priority = priority,
            Note = note,
            CreatedAt = Now,
            UpdatedAt = Now,
        };
}
