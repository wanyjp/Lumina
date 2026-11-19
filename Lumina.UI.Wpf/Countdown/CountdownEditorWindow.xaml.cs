using System.Windows;
using System.Windows.Controls;
using Lumina.Domain.Countdowns;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Countdowns;

namespace Lumina.UI.Wpf.Countdown;

/// <summary>
/// 倒计时编辑窗（P3-04）：名称、分类、重复方式与锚点、七档提前提醒。
/// 切换重复方式会清空旧锚点；保存时由服务层再次归一化与校验。
/// </summary>
public partial class CountdownEditorWindow : Window
{
    private static readonly (CountdownCategory Value, string Label)[] Categories =
    [
        (CountdownCategory.Work, "工作"),
        (CountdownCategory.Life, "生活"),
        (CountdownCategory.Exam, "考试"),
        (CountdownCategory.Anniversary, "纪念日"),
        (CountdownCategory.Other, "其他"),
    ];

    private static readonly (ReminderOffset Value, string Label)[] Reminders =
    [
        (ReminderOffset.None, "不提醒"),
        (ReminderOffset.OnTime, "准时"),
        (ReminderOffset.Minutes5, "提前 5 分钟"),
        (ReminderOffset.Minutes15, "提前 15 分钟"),
        (ReminderOffset.Minutes30, "提前 30 分钟"),
        (ReminderOffset.Hour1, "提前 1 小时"),
        (ReminderOffset.Day1, "提前 1 天"),
    ];

    private static readonly string[] WeekdayLabels =
    [
        "周日", "周一", "周二", "周三", "周四", "周五", "周六",
    ];

    private readonly CountdownItem? _existing;
    private readonly DateOnly _today;
    private CountdownItem _working;
    private bool _loading = true;

    public CountdownEditorWindow(
        CountdownItem? existing,
        DateOnly today,
        ReminderOffset defaultReminder)
    {
        _existing = existing;
        _today = today;
        _working = existing ?? new CountdownItem
        {
            Name = string.Empty,
            Category = CountdownCategory.Other,
            Repeat = CountdownRepeat.None,
            Reminder = defaultReminder,
            AnchorDate = today.AddDays(7),
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };

        InitializeComponent();
        HeaderText.Text = existing is null ? "新增倒计时" : "编辑倒计时";
        Title = HeaderText.Text;

        BuildCategoryChoices();
        BuildRepeatChoices();
        BuildReminderChoices();
        BuildWeekdayChoices();
        BuildDayChoices();
        FillFromWorking();

        _loading = false;
        ShowRepeatPanel(_working.Repeat);
        UpdatePreview();
        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    public CountdownItem? Result { get; private set; }

    private void BuildCategoryChoices()
    {
        foreach (var (value, label) in Categories)
        {
            CategoryPanel.Children.Add(new RadioButton
            {
                Content = label,
                Tag = value,
                GroupName = "CountdownCategory",
                Style = (Style)FindResource("Theme.ThemeChoice"),
                Margin = new Thickness(0, 0, 8, 8),
                IsChecked = _working.Category == value,
            });
        }
    }

    private void BuildRepeatChoices()
    {
        RepeatBox.ItemsSource = new[]
        {
            new RepeatOption(CountdownRepeat.None, "不重复"),
            new RepeatOption(CountdownRepeat.Weekly, "每周"),
            new RepeatOption(CountdownRepeat.Monthly, "每月"),
            new RepeatOption(CountdownRepeat.Yearly, "每年"),
        };
        RepeatBox.SelectedItem = RepeatBox.Items
            .OfType<RepeatOption>()
            .First(option => option.Value == _working.Repeat);
    }

    private void BuildReminderChoices()
    {
        ReminderBox.ItemsSource = Reminders
            .Select(item => new ReminderOption(item.Value, item.Label))
            .ToArray();
        ReminderBox.SelectedItem = ReminderBox.Items
            .OfType<ReminderOption>()
            .First(option => option.Value == _working.Reminder);
    }

    private void BuildWeekdayChoices()
    {
        WeekdayBox.ItemsSource = WeekdayLabels;
    }

    private void BuildDayChoices()
    {
        MonthlyDayBox.ItemsSource = Enumerable.Range(1, 31).ToArray();
        MonthBox.ItemsSource = Enumerable.Range(1, 12).ToArray();
        RebuildYearlyDays(_working.AnchorMonth ?? _today.Month);
    }

    private void FillFromWorking()
    {
        NameBox.Text = _working.Name;

        OnceDatePicker.SelectedDate =
            (_working.AnchorDate ?? _today).ToDateTime(TimeOnly.MinValue);

        WeekdayBox.SelectedIndex = _working.AnchorWeekday is { } weekday && weekday is >= 0 and <= 6
            ? weekday
            : (int)_today.DayOfWeek;

        MonthlyDayBox.SelectedItem = _working.AnchorDay is { } day && day is >= 1 and <= 31
            ? day
            : _today.Day;
        MonthEndCheck.IsChecked = _working.AnchorMonthEnd;

        var month = _working.AnchorMonth is { } value && value is >= 1 and <= 12
            ? value
            : _today.Month;
        MonthBox.SelectedItem = month;
        RebuildYearlyDays(month);
        YearlyDayBox.SelectedItem = _working.AnchorDay is { } yearlyDay
            ? Math.Clamp(yearlyDay, 1, DaysInYearlyMonth(month))
            : Math.Min(_today.Day, DaysInYearlyMonth(month));
        MonthlyDayBox.IsEnabled = _working.AnchorMonthEnd != true;
    }

    private void ShowRepeatPanel(CountdownRepeat repeat)
    {
        OncePanel.Visibility = repeat == CountdownRepeat.None ? Visibility.Visible : Visibility.Collapsed;
        WeeklyPanel.Visibility = repeat == CountdownRepeat.Weekly ? Visibility.Visible : Visibility.Collapsed;
        MonthlyPanel.Visibility = repeat == CountdownRepeat.Monthly ? Visibility.Visible : Visibility.Collapsed;
        YearlyPanel.Visibility = repeat == CountdownRepeat.Yearly ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRepeatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var repeat = SelectedRepeat();
        _working = CountdownCalendar.WithRepeat(_working, repeat);
        ResetAnchorInputs(repeat);
        ShowRepeatPanel(repeat);
        UpdatePreview();
    }

    private void ResetAnchorInputs(CountdownRepeat repeat)
    {
        switch (repeat)
        {
            case CountdownRepeat.Weekly:
                WeekdayBox.SelectedIndex = (int)_today.DayOfWeek;
                break;
            case CountdownRepeat.Monthly:
                MonthlyDayBox.SelectedItem = _today.Day;
                MonthEndCheck.IsChecked = false;
                break;
            case CountdownRepeat.Yearly:
                MonthBox.SelectedItem = _today.Month;
                RebuildYearlyDays(_today.Month);
                YearlyDayBox.SelectedItem = Math.Min(_today.Day, DaysInYearlyMonth(_today.Month));
                break;
            default:
                OnceDatePicker.SelectedDate = _today.AddDays(7).ToDateTime(TimeOnly.MinValue);
                break;
        }
    }

    private void OnMonthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var month = SelectedMonth();
        var previous = YearlyDayBox.SelectedItem is int day ? day : _today.Day;
        RebuildYearlyDays(month);
        YearlyDayBox.SelectedItem = Math.Clamp(previous, 1, DaysInYearlyMonth(month));
        UpdatePreview();
    }

    private void RebuildYearlyDays(int month)
    {
        var days = DaysInYearlyMonth(month);
        YearlyDayBox.ItemsSource = Enumerable.Range(1, days).ToArray();
    }

    private void OnMonthEndChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        MonthlyDayBox.IsEnabled = MonthEndCheck.IsChecked != true;
        UpdatePreview();
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e) => UpdatePreview();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void OnDateChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (_loading)
        {
            return;
        }

        try
        {
            var draft = BuildDraft();
            var snapshot = CountdownCalendar.Evaluate(draft, _today);
            PreviewText.Text = $"{CountdownText.Semantic(draft, snapshot)} · {CountdownText.Remaining(snapshot)}";
        }
        catch (ArgumentException)
        {
            PreviewText.Text = "填写名称并选择日期后，这里会预览下次发生日。";
        }
    }

    private CountdownItem BuildDraft()
    {
        var repeat = SelectedRepeat();
        var name = NameBox.Text.Trim();
        if (name.Length == 0)
        {
            throw new ArgumentException("请输入倒计时名称。");
        }

        var item = _working with
        {
            Name = name,
            Category = SelectedCategory(),
            Repeat = repeat,
            Reminder = SelectedReminder(),
        };

        var monthEnd = MonthEndCheck.IsChecked == true;
        return repeat switch
        {
            CountdownRepeat.Weekly => CountdownCalendar.WithAnchor(item, DateOfWeekday(SelectedWeekday())),
            CountdownRepeat.Monthly => CountdownCalendar.WithAnchor(
                item,
                new DateOnly(2000, 1, SelectedMonthlyDay()),
                monthEnd),
            CountdownRepeat.Yearly => CountdownCalendar.WithAnchor(
                item,
                new DateOnly(2000, SelectedMonth(), SelectedYearlyDay())),
            _ => CountdownCalendar.WithAnchor(item, SelectedDate()),
        };
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        try
        {
            var draft = BuildDraft();
            var now = DateTimeOffset.Now;
            Result = draft with
            {
                CreatedAt = _existing?.CreatedAt ?? now,
                UpdatedAt = now,
            };
            DialogResult = true;
        }
        catch (ArgumentException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private CountdownCategory SelectedCategory()
    {
        foreach (var child in CategoryPanel.Children)
        {
            if (child is RadioButton { IsChecked: true, Tag: CountdownCategory category })
            {
                return category;
            }
        }

        return CountdownCategory.Other;
    }

    private CountdownRepeat SelectedRepeat() =>
        RepeatBox.SelectedItem is RepeatOption option ? option.Value : CountdownRepeat.None;

    private ReminderOffset SelectedReminder() =>
        ReminderBox.SelectedItem is ReminderOption option ? option.Value : ReminderOffset.Day1;

    private int SelectedWeekday() =>
        WeekdayBox.SelectedIndex is >= 0 and <= 6 ? WeekdayBox.SelectedIndex : (int)_today.DayOfWeek;

    private int SelectedMonthlyDay() =>
        MonthlyDayBox.SelectedItem is int day ? day : _today.Day;

    private int SelectedMonth() =>
        MonthBox.SelectedItem is int month ? month : _today.Month;

    private int SelectedYearlyDay() =>
        YearlyDayBox.SelectedItem is int day ? day : Math.Min(_today.Day, DaysInYearlyMonth(SelectedMonth()));

    private DateOnly SelectedDate()
    {
        if (OnceDatePicker.SelectedDate is not { } date)
        {
            throw new ArgumentException("请选择目标日期。");
        }

        return DateOnly.FromDateTime(date);
    }

    private DateOnly DateOfWeekday(int weekday)
    {
        var offset = ((weekday - (int)_today.DayOfWeek) + 7) % 7;
        return _today.AddDays(offset);
    }

    private static int DaysInYearlyMonth(int month) =>
        DateTime.DaysInMonth(2000, Math.Clamp(month, 1, 12));

    private sealed record RepeatOption(CountdownRepeat Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ReminderOption(ReminderOffset Value, string Label)
    {
        public override string ToString() => Label;
    }
}
