using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Calendar;
using Lumina.UI.Wpf.Controls;

namespace Lumina.UI.Wpf.Calendar;

/// <summary>
/// P1-06 / P1-07 日程表单：标题、起止日期与时间、地点、备注、优先级、提醒与周期规则。
/// 周期开启时禁用日程结束日期（周期实例为单日），并实时预览前 5 个命中日期。
/// </summary>
public partial class ScheduleEditorWindow : Window
{
    private static readonly (string Key, string Label)[] PriorityOptions =
    [
        ("high", "高"),
        ("normal", "中"),
        ("low", "低"),
    ];

    private static readonly (ReminderOffset Offset, string Label)[] ReminderOptions =
    [
        (ReminderOffset.None, "不提醒"),
        (ReminderOffset.OnTime, "准时"),
        (ReminderOffset.Minutes5, "提前 5 分钟"),
        (ReminderOffset.Minutes15, "提前 15 分钟"),
        (ReminderOffset.Minutes30, "提前 30 分钟"),
        (ReminderOffset.Hour1, "提前 1 小时"),
        (ReminderOffset.Day1, "提前 1 天"),
    ];

    private static readonly (RepeatMode Mode, string Label)[] RepeatModeOptions =
    [
        (RepeatMode.Week, "星期区间"),
        (RepeatMode.Month, "每月区间"),
        (RepeatMode.Weekdays, "星期多选"),
        (RepeatMode.MonthDay, "每月日期"),
        (RepeatMode.Daily, "每天"),
        (RepeatMode.Workday, "法定工作日"),
        (RepeatMode.Holiday, "法定节假日"),
    ];

    private static readonly (RepeatEndType EndType, string Label)[] RepeatEndOptions =
    [
        (RepeatEndType.Date, "结束日期"),
        (RepeatEndType.Count, "按次数"),
        (RepeatEndType.Forever, "永久"),
    ];

    private static readonly (string Label, int Day)[] WeekdayOptions =
    [
        ("一", 1), ("二", 2), ("三", 3), ("四", 4), ("五", 5), ("六", 6), ("日", 0),
    ];

    private readonly ScheduleEvent? _existing;

    private DateTime? _endDateBeforeRepeat;
    private bool _suppressRepeat;

    public ScheduleEditorWindow(
        ScheduleEvent? existing,
        DateOnly defaultDate,
        ReminderOffset defaultReminder)
    {
        InitializeComponent();

        _existing = existing;
        HeaderText.Text = existing is null ? "新建日程" : "编辑日程";
        Title = HeaderText.Text;

        TimeChoice.Fill(StartTimeBox);
        TimeChoice.Fill(EndTimeBox);
        FillBoundChoices(WeekStartBox, 1, 7, includeEnd: false);
        FillBoundChoices(WeekEndBox, 1, 7, includeEnd: false);
        FillBoundChoices(MonthStartBox, 1, 31, includeEnd: false);
        FillBoundChoices(MonthEndBox, 1, 31, includeEnd: true);
        FillBoundChoices(MonthDayBox, 1, 31, includeEnd: true);

        BuildPriorityChoices();
        BuildReminderChoices();
        BuildRepeatChoices();
        AttachRepeatHandlers();

        _suppressRepeat = true;
        try
        {
            ApplyRepeatDefaults(defaultDate);

            if (existing is null)
            {
                StartDatePicker.SelectedDate = defaultDate.ToDateTime(TimeOnly.MinValue);
                SelectReminder(defaultReminder);
                SelectPriority(Priority.Normal);
            }
            else
            {
                FillExisting(existing);
            }
        }
        finally
        {
            _suppressRepeat = false;
        }

        UpdatePreview();

        Loaded += (_, _) => TitleBox.Focus();
    }

    public ScheduleEvent? Result { get; private set; }

    private void FillExisting(ScheduleEvent existing)
    {
        TitleBox.Text = existing.Title;
        StartDatePicker.SelectedDate = existing.StartDate.ToDateTime(TimeOnly.MinValue);
        TimeChoice.Select(StartTimeBox, existing.StartTime);
        EndDatePicker.SelectedDate = existing.EndDate?.ToDateTime(TimeOnly.MinValue);
        TimeChoice.Select(EndTimeBox, existing.EndTime);
        LocationBox.Text = existing.Location;
        NoteBox.Text = existing.Note ?? string.Empty;
        SelectPriority(existing.Priority);
        SelectReminder(existing.Reminder);

        if (existing.Repeat is null)
        {
            return;
        }

        RepeatToggle.IsChecked = true;
        _endDateBeforeRepeat = existing.EndDate?.ToDateTime(TimeOnly.MinValue);
        EndDatePicker.SelectedDate = null;
        EndDatePicker.IsEnabled = false;
        RepeatPanel.Visibility = Visibility.Visible;
        RepeatHint.Text = "周期日程按规则生成实例，暂不支持跨天。";

        var rule = existing.Repeat;
        SelectRepeatMode(rule.Mode);
        SelectEndType(rule.EndType);

        if (rule.WeekStart is { } weekStart)
        {
            WeekStartBox.Text = weekStart.ToString(CultureInfo.InvariantCulture);
        }

        if (rule.WeekEnd is { } weekEnd)
        {
            WeekEndBox.Text = weekEnd.ToString(CultureInfo.InvariantCulture);
        }

        if (rule.MonthStart is { } monthStart)
        {
            MonthStartBox.Text = monthStart.ToString(CultureInfo.InvariantCulture);
        }

        if (rule.MonthEnd is { } monthEnd)
        {
            MonthEndBox.Text = monthEnd;
        }

        if (rule.MonthDay is { } monthDay)
        {
            MonthDayBox.Text = monthDay;
        }

        if (rule.Weekdays is { } weekdays)
        {
            foreach (var child in WeekdaysPanel.Children)
            {
                if (child is CheckBox { Tag: int day } checkbox)
                {
                    checkbox.IsChecked = weekdays.Contains(day);
                }
            }
        }

        if (rule.EndDate is { } repeatEndDate)
        {
            RepeatEndDatePicker.SelectedDate = repeatEndDate.ToDateTime(TimeOnly.MinValue);
        }

        if (rule.Count is { } count)
        {
            RepeatCountBox.Text = count.ToString(CultureInfo.InvariantCulture);
        }

        UpdateRepeatPanels();
        UpdateEndTypePanels();
    }

    private void BuildPriorityChoices()
    {
        foreach (var (key, label) in PriorityOptions)
        {
            PriorityPanel.Children.Add(new RadioButton
            {
                Content = label,
                Tag = key,
                GroupName = "SchedulePriority",
                Style = (Style)FindResource("Theme.ThemeChoice"),
                Margin = new Thickness(0, 0, 8, 8),
            });
        }
    }

    private void BuildReminderChoices()
    {
        foreach (var (offset, label) in ReminderOptions)
        {
            ReminderPanel.Children.Add(new RadioButton
            {
                Content = label,
                Tag = offset,
                GroupName = "ScheduleReminder",
                Style = (Style)FindResource("Theme.ThemeChoice"),
                Margin = new Thickness(0, 0, 8, 8),
            });
        }
    }

    private void BuildRepeatChoices()
    {
        foreach (var (mode, label) in RepeatModeOptions)
        {
            var choice = new RadioButton
            {
                Content = label,
                Tag = mode,
                GroupName = "RepeatMode",
                Style = (Style)FindResource("Theme.ThemeChoice"),
                Margin = new Thickness(0, 0, 8, 8),
            };
            choice.Checked += OnRepeatModeChanged;
            RepeatModePanel.Children.Add(choice);
        }

        foreach (var (endType, label) in RepeatEndOptions)
        {
            var choice = new RadioButton
            {
                Content = label,
                Tag = endType,
                GroupName = "RepeatEndType",
                Style = (Style)FindResource("Theme.ThemeChoice"),
                Margin = new Thickness(0, 0, 8, 8),
            };
            choice.Checked += OnEndTypeChanged;
            EndTypePanel.Children.Add(choice);
        }

        foreach (var (label, day) in WeekdayOptions)
        {
            WeekdaysPanel.Children.Add(new CheckBox
            {
                Content = label,
                Tag = day,
                Style = (Style)FindResource("Theme.Toggle"),
                Margin = new Thickness(0, 0, 10, 6),
            });
        }
    }

    private void AttachRepeatHandlers()
    {
        foreach (var textBox in new[] { RepeatCountBox })
        {
            textBox.TextChanged += (_, _) => UpdatePreview();
        }

        var comboBoxes = new[] { WeekStartBox, WeekEndBox, MonthStartBox, MonthEndBox, MonthDayBox };
        foreach (var comboBox in comboBoxes)
        {
            // 可编辑 ComboBox 的 TextChanged 由内部 TextBox 冒泡上来。
            comboBox.AddHandler(
                TextBoxBase.TextChangedEvent,
                new TextChangedEventHandler((_, _) => UpdatePreview()),
                handledEventsToo: true);
            comboBox.DropDownClosed += (_, _) => UpdatePreview();
        }

        RepeatEndDatePicker.SelectedDateChanged += (_, _) => UpdatePreview();

        foreach (var child in WeekdaysPanel.Children)
        {
            if (child is CheckBox checkbox)
            {
                checkbox.Checked += (_, _) => UpdatePreview();
                checkbox.Unchecked += (_, _) => UpdatePreview();
            }
        }
    }

    private void ApplyRepeatDefaults(DateOnly startDate)
    {
        SelectRepeatMode(RepeatMode.Week);
        WeekStartBox.Text = "1";
        WeekEndBox.Text = "5";
        MonthStartBox.Text = "1";
        MonthEndBox.Text = "end";
        MonthDayBox.Text = "1";
        RepeatEndDatePicker.SelectedDate = startDate.AddDays(30).ToDateTime(TimeOnly.MinValue);
        RepeatCountBox.Text = "10";
        SelectEndType(RepeatEndType.Forever);

        foreach (var child in WeekdaysPanel.Children)
        {
            if (child is CheckBox { Tag: int day } checkbox)
            {
                checkbox.IsChecked = day is >= 1 and <= 5;
            }
        }

        UpdateRepeatPanels();
        UpdateEndTypePanels();
        RepeatPreviewText.Text = string.Empty;
    }

    private void SelectPriority(Priority priority)
    {
        var key = priority switch
        {
            Priority.High => "high",
            Priority.Low => "low",
            _ => "normal",
        };

        foreach (var child in PriorityPanel.Children)
        {
            if (child is RadioButton choice)
            {
                choice.IsChecked = (string?)choice.Tag == key;
            }
        }
    }

    private void SelectReminder(ReminderOffset reminder)
    {
        foreach (var child in ReminderPanel.Children)
        {
            if (child is RadioButton { Tag: ReminderOffset offset } choice)
            {
                choice.IsChecked = offset == reminder;
            }
        }
    }

    private void SelectRepeatMode(RepeatMode mode)
    {
        foreach (var child in RepeatModePanel.Children)
        {
            if (child is RadioButton { Tag: RepeatMode value } choice)
            {
                choice.IsChecked = value == mode;
            }
        }
    }

    private void SelectEndType(RepeatEndType endType)
    {
        foreach (var child in EndTypePanel.Children)
        {
            if (child is RadioButton { Tag: RepeatEndType value } choice)
            {
                choice.IsChecked = value == endType;
            }
        }
    }

    private void OnRepeatToggleClick(object sender, RoutedEventArgs e)
    {
        if (_suppressRepeat)
        {
            return;
        }

        if (RepeatToggle.IsChecked == true)
        {
            _endDateBeforeRepeat = EndDatePicker.SelectedDate;
            EndDatePicker.SelectedDate = null;
            EndDatePicker.IsEnabled = false;
            RepeatPanel.Visibility = Visibility.Visible;
            RepeatHint.Text = "周期日程按规则生成实例，暂不支持跨天。";
            UpdateRepeatPanels();
            UpdateEndTypePanels();
            UpdatePreview();
        }
        else
        {
            RepeatPanel.Visibility = Visibility.Collapsed;
            EndDatePicker.IsEnabled = true;
            EndDatePicker.SelectedDate = _endDateBeforeRepeat;
            RepeatHint.Text = string.Empty;
            RepeatPreviewText.Text = string.Empty;
        }
    }

    private void OnRepeatModeChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressRepeat)
        {
            return;
        }

        UpdateRepeatPanels();
        UpdatePreview();
    }

    private void OnEndTypeChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressRepeat)
        {
            return;
        }

        UpdateEndTypePanels();
        UpdatePreview();
    }

    private void UpdateRepeatPanels()
    {
        var mode = SelectedRepeatMode();
        WeekRangePanel.Visibility = mode == RepeatMode.Week ? Visibility.Visible : Visibility.Collapsed;
        MonthRangePanel.Visibility = mode == RepeatMode.Month ? Visibility.Visible : Visibility.Collapsed;
        WeekdaysPanel.Visibility = mode == RepeatMode.Weekdays ? Visibility.Visible : Visibility.Collapsed;
        MonthDayPanel.Visibility = mode == RepeatMode.MonthDay ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEndTypePanels()
    {
        var endType = SelectedEndType();
        RepeatEndDatePanel.Visibility = endType == RepeatEndType.Date ? Visibility.Visible : Visibility.Collapsed;
        RepeatCountPanel.Visibility = endType == RepeatEndType.Count ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdatePreview()
    {
        if (_suppressRepeat || RepeatToggle.IsChecked != true)
        {
            if (RepeatToggle.IsChecked != true)
            {
                RepeatPreviewText.Text = string.Empty;
            }

            return;
        }

        if (StartDatePicker.SelectedDate is not { } startDateTime)
        {
            RepeatPreviewText.Text = "请先选择开始日期。";
            return;
        }

        var startDate = DateOnly.FromDateTime(startDateTime);
        var rule = BuildRepeatRule(startDate, out var error);

        if (rule is null)
        {
            RepeatPreviewText.Text = error ?? "周期设置无效。";
            return;
        }

        var dates = RepeatRuleEngine.Preview(rule, startDate);
        RepeatPreviewText.Text = dates.Count == 0
            ? "该规则没有命中日期，无法保存。"
            : $"前 {dates.Count} 次：{string.Join("、", dates.Select(date => date.ToString("MM/dd")))}";
    }

    private RepeatRule? BuildRepeatRule(DateOnly startDate, out string? error)
    {
        error = null;
        var mode = SelectedRepeatMode();
        var endType = SelectedEndType();
        var rule = new RepeatRule
        {
            Mode = mode,
            EndType = endType,
        };

        switch (mode)
        {
            case RepeatMode.Week:
                var weekStart = ParseInt(WeekStartBox.Text, 1, 7);
                var weekEnd = ParseInt(WeekEndBox.Text, 1, 7);

                if (weekStart is null || weekEnd is null)
                {
                    error = "星期区间应为 1–7（1=周一，7=周日）。";
                    return null;
                }

                rule = rule with { WeekStart = weekStart, WeekEnd = weekEnd };
                break;

            case RepeatMode.Month:
                var monthStart = ParseInt(MonthStartBox.Text, 1, 31);
                var monthEnd = NormalizeBound(MonthEndBox.Text);

                if (monthStart is null || monthEnd is null)
                {
                    error = "每月区间应为 1–31 或 end。";
                    return null;
                }

                rule = rule with { MonthStart = monthStart, MonthEnd = monthEnd };
                break;

            case RepeatMode.Weekdays:
                var weekdays = WeekdaysPanel.Children
                    .OfType<CheckBox>()
                    .Where(checkbox => checkbox.IsChecked == true)
                    .Select(checkbox => (int)checkbox.Tag)
                    .ToArray();

                if (weekdays.Length == 0)
                {
                    error = "请至少选择一个星期。";
                    return null;
                }

                rule = rule with { Weekdays = weekdays };
                break;

            case RepeatMode.MonthDay:
                var monthDay = NormalizeBound(MonthDayBox.Text);

                if (monthDay is null)
                {
                    error = "每月日期应为 1–31 或 end。";
                    return null;
                }

                rule = rule with { MonthDay = monthDay };
                break;

            default:
                break;
        }

        switch (endType)
        {
            case RepeatEndType.Date:
                if (RepeatEndDatePicker.SelectedDate is not { } repeatEnd)
                {
                    error = "请选择周期结束日期。";
                    return null;
                }

                rule = rule with { EndDate = DateOnly.FromDateTime(repeatEnd) };
                break;

            case RepeatEndType.Count:
                var count = ParseInt(RepeatCountBox.Text, 1, RepeatRuleValidator.MaxCount);

                if (count is null)
                {
                    error = $"周期次数应在 1–{RepeatRuleValidator.MaxCount} 之间。";
                    return null;
                }

                rule = rule with { Count = count };
                break;

            default:
                break;
        }

        try
        {
            RepeatRuleValidator.Validate(rule, startDate);
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return null;
        }

        return rule;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var title = TitleBox.Text.Trim();
        if (title.Length == 0)
        {
            ShowError("请输入日程标题。");
            return;
        }

        if (StartDatePicker.SelectedDate is not { } startDateTime)
        {
            ShowError("请选择开始日期。");
            return;
        }

        var startDate = DateOnly.FromDateTime(startDateTime);

        if (!TimeChoice.TryRead(StartTimeBox, out var startTime, out var timeError))
        {
            ShowError($"开始时间：{timeError}");
            return;
        }

        if (!TimeChoice.TryRead(EndTimeBox, out var endTime, out timeError))
        {
            ShowError($"结束时间：{timeError}");
            return;
        }

        DateOnly? endDate = EndDatePicker.SelectedDate is { } endDateTime
            ? DateOnly.FromDateTime(endDateTime)
            : null;

        if (endDate is { } end && end < startDate)
        {
            ShowError("结束日期不能早于开始日期。");
            return;
        }

        if (startTime is null && endTime is not null)
        {
            ShowError("设置结束时间前请先设置开始时间。");
            return;
        }

        if (startTime is { } from && endTime is { } to)
        {
            var sameDay = endDate is null || endDate == startDate;
            if (sameDay && to < from)
            {
                ShowError("同一天的结束时间不能早于开始时间。");
                return;
            }
        }

        RepeatRule? repeat = null;

        if (RepeatToggle.IsChecked == true)
        {
            repeat = BuildRepeatRule(startDate, out var repeatError);

            if (repeat is null)
            {
                ShowError(repeatError ?? "周期设置无效。");
                return;
            }

            if (RepeatRuleEngine.Preview(repeat, startDate, 1).Count == 0)
            {
                ShowError("该规则没有命中日期，无法保存。");
                return;
            }
        }

        var note = NoteBox.Text.Trim();

        Result = new ScheduleEvent
        {
            Id = _existing?.Id ?? 0,
            LegacyId = _existing?.LegacyId,
            Title = title,
            StartDate = startDate,
            StartTime = startTime,
            EndDate = repeat is null ? endDate : null,
            EndTime = endTime,
            Location = LocationBox.Text.Trim(),
            Note = note.Length == 0 ? null : note,
            Priority = SelectedPriority(),
            Repeat = repeat,
            Reminder = SelectedReminder(),
            Done = _existing?.Done ?? false,
        };

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private Priority SelectedPriority()
    {
        foreach (var child in PriorityPanel.Children)
        {
            if (child is RadioButton { IsChecked: true, Tag: string key })
            {
                return key switch
                {
                    "high" => Priority.High,
                    "low" => Priority.Low,
                    _ => Priority.Normal,
                };
            }
        }

        return Priority.Normal;
    }

    private ReminderOffset SelectedReminder()
    {
        foreach (var child in ReminderPanel.Children)
        {
            if (child is RadioButton { IsChecked: true, Tag: ReminderOffset offset })
            {
                return offset;
            }
        }

        return ReminderOffset.None;
    }

    private RepeatMode SelectedRepeatMode()
    {
        foreach (var child in RepeatModePanel.Children)
        {
            if (child is RadioButton { IsChecked: true, Tag: RepeatMode mode })
            {
                return mode;
            }
        }

        return RepeatMode.Week;
    }

    private RepeatEndType SelectedEndType()
    {
        foreach (var child in EndTypePanel.Children)
        {
            if (child is RadioButton { IsChecked: true, Tag: RepeatEndType endType })
            {
                return endType;
            }
        }

        return RepeatEndType.Forever;
    }

    /// <summary>周期边界输入：可编辑下拉，既能选 1–31 / end，也能直接输入。</summary>
    private static void FillBoundChoices(ComboBox box, int min, int max, bool includeEnd)
    {
        var items = Enumerable.Range(min, max - min + 1)
            .Select(value => value.ToString(CultureInfo.InvariantCulture))
            .ToList();

        if (includeEnd)
        {
            items.Add("end");
        }

        box.IsEditable = true;
        box.IsTextSearchEnabled = false;
        box.ItemsSource = items;
        box.Text = items[0];
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string selected)
            {
                box.Text = selected;
            }
        };
    }

    private static int? ParseInt(string text, int minimum, int maximum)
    {
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && value >= minimum
            && value <= maximum
            ? value
            : null;
    }

    private static string? NormalizeBound(string text)
    {
        var trimmed = text.Trim();

        if (string.Equals(trimmed, "end", StringComparison.OrdinalIgnoreCase))
        {
            return "end";
        }

        var value = ParseInt(trimmed, 1, 31);
        return value?.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>时间下拉项：首项表示“不设置（全天）”</summary>
    private void ShowError(string message) => ErrorText.Text = message;
}
