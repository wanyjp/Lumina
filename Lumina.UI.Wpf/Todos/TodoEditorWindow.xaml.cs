using System.Windows;
using System.Windows.Controls;
using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.UI.Wpf.Controls;

namespace Lumina.UI.Wpf.Todos;

/// <summary>P1-09 待办表单：标题、截止日期/时间、优先级与备注。</summary>
public partial class TodoEditorWindow : Window
{
    private static readonly (string Key, string Label)[] PriorityOptions =
    [
        ("high", "高"),
        ("normal", "中"),
        ("low", "低"),
    ];

    private readonly TodoItem? _existing;

    public TodoEditorWindow(TodoItem? existing, DateOnly defaultDate)
    {
        InitializeComponent();

        _existing = existing;
        HeaderText.Text = existing is null ? "新建待办" : "编辑待办";
        Title = HeaderText.Text;

        BuildPriorityChoices();
        TimeChoice.Fill(DueTimeBox);

        if (existing is null)
        {
            DueDatePicker.SelectedDate = defaultDate.ToDateTime(TimeOnly.MinValue);
            SelectPriority(Priority.Normal);
        }
        else
        {
            TextBox.Text = existing.Text;
            DueDatePicker.SelectedDate = existing.DueDate?.ToDateTime(TimeOnly.MinValue);
            TimeChoice.Select(DueTimeBox, existing.DueTime);
            NoteBox.Text = existing.Note;
            SelectPriority(existing.Priority);
        }

        Loaded += (_, _) => TextBox.Focus();
    }

    public TodoItem? Result { get; private set; }

    private void BuildPriorityChoices()
    {
        foreach (var (key, label) in PriorityOptions)
        {
            PriorityPanel.Children.Add(new RadioButton
            {
                Content = label,
                Tag = key,
                GroupName = "TodoPriority",
                Style = (Style)FindResource("Theme.ThemeChoice"),
                Margin = new Thickness(0, 0, 8, 8),
            });
        }
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

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;

        var text = TextBox.Text.Trim();
        if (text.Length == 0)
        {
            ErrorText.Text = "请输入待办标题。";
            return;
        }

        if (!TimeChoice.TryRead(DueTimeBox, out var dueTime, out var timeError))
        {
            ErrorText.Text = $"截止时间：{timeError}";
            return;
        }

        DateOnly? dueDate = DueDatePicker.SelectedDate is { } selected
            ? DateOnly.FromDateTime(selected)
            : null;

        if (dueTime is not null && dueDate is null)
        {
            ErrorText.Text = "设置截止时间前请先设置截止日期。";
            return;
        }

        var now = DateTimeOffset.Now;
        var note = NoteBox.Text.Trim();

        Result = new TodoItem
        {
            Id = _existing?.Id ?? 0,
            LegacyId = _existing?.LegacyId,
            Text = text,
            DueDate = dueDate,
            DueTime = dueTime,
            Priority = SelectedPriority(),
            Done = _existing?.Done ?? false,
            DoneAt = _existing?.DoneAt,
            Note = note,
            CreatedAt = _existing?.CreatedAt ?? now,
            UpdatedAt = _existing?.UpdatedAt ?? now,
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

}
