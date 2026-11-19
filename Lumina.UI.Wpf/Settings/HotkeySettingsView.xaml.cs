using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lumina.Domain.Enums;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Lumina.Services.Hotkeys;

namespace Lumina.UI.Wpf.Settings;

/// <summary>
/// P1-11 统一快捷键表：9 项录制、清空、单项恢复、全部恢复与全表冲突检测。
/// 冲突或系统占用时保留原值；空绑定不参与冲突。
/// </summary>
public partial class HotkeySettingsView : UserControl
{
    private readonly SettingsContext _context;

    private string? _recordingAction;
    private Button? _recordingButton;
    private HotkeyScope _recordingScope = HotkeyScope.Global;

    public HotkeySettingsView(SettingsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;

        InitializeComponent();

        ResetAllButton.Click += OnResetAllClick;
        PreviewKeyDown += OnRecordingKeyDown;
        LostKeyboardFocus += OnLostKeyboardFocus;

        BuildRows();
    }

    private void BuildRows()
    {
        RowsPanel.Children.Clear();

        var globalCount = 0;
        var moduleCount = 0;

        foreach (var info in HotkeyActionCatalog.All)
        {
            var binding = _context.HotkeyBindings.Get(info.Action);
            if (HotkeyCatalog.IsUnbound(binding))
            {
                binding = HotkeyCatalog.Unbound(HotkeyCatalog.Defaults[info.Action].Scope);
            }

            if (binding.Scope == HotkeyScope.Global)
            {
                globalCount++;
            }
            else
            {
                moduleCount++;
            }

            RowsPanel.Children.Add(BuildRow(info, binding));
        }

        SummaryText.Text =
            $"全局 {globalCount} 项 · 模块内 {moduleCount} 项；冲突会阻止保存并保留原值，空绑定不参与冲突。";
    }

    private FrameworkElement BuildRow(HotkeyActionInfo info, HotkeyGesture binding)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var scopeChip = new Border
        {
            Style = (Style)FindResource("Theme.Chip"),
            Padding = new Thickness(7, 1, 7, 1),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = binding.Scope == HotkeyScope.Global ? "全局" : "模块内",
                FontSize = 11,
                Foreground = binding.Scope == HotkeyScope.Global
                    ? (Brush)FindResource("Theme.AccentBrush")
                    : (Brush)FindResource("Theme.TextTertiaryBrush"),
            },
        };
        grid.Children.Add(scopeChip);

        var text = new StackPanel
        {
            Margin = new Thickness(10, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Children.Add(new TextBlock
        {
            Text = info.Title,
            Style = (Style)FindResource("Theme.Text.Base"),
            FontWeight = FontWeights.SemiBold,
        });
        text.Children.Add(new TextBlock
        {
            Text = info.Summary,
            Style = (Style)FindResource("Theme.Text.Tertiary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var gestureText = new TextBlock
        {
            Text = HotkeyCatalog.IsUnbound(binding) ? "未设置" : binding.ToString(),
            FontFamily = (FontFamily)FindResource("Theme.FontFamilyMono"),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = HotkeyCatalog.IsUnbound(binding)
                ? (Brush)FindResource("Theme.TextTertiaryBrush")
                : (Brush)FindResource("Theme.TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(gestureText, 2);
        grid.Children.Add(gestureText);

        var statusText = new TextBlock
        {
            Text = ResolveStatus(info, binding),
            Style = (Style)FindResource("Theme.Text.Caption"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(statusText, 3);
        grid.Children.Add(statusText);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var record = SmallButton("录制", info.Action, OnRecordClick);
        var clear = SmallButton("清空", info.Action, OnClearClick);
        var reset = SmallButton("恢复", info.Action, OnResetClick);

        actions.Children.Add(record);
        actions.Children.Add(clear);
        actions.Children.Add(reset);
        Grid.SetColumn(actions, 4);
        grid.Children.Add(actions);

        return grid;
    }

    private string ResolveStatus(HotkeyActionInfo info, HotkeyGesture binding)
    {
        if (HotkeyCatalog.IsUnbound(binding))
        {
            return "未设置";
        }

        if (binding.Scope == HotkeyScope.Module)
        {
            return "截图页内生效";
        }

        if (!HotkeyRegistrationService.IsActiveGlobalAction(info.Action))
        {
            return "未接入";
        }

        // 与启动注册同一口径：已注册的组合重复调用是幂等的，
        // 返回 false 说明被其他程序占用，用户可据此直接改键。
        return _context.Hotkeys.Register(info.Action, binding)
            ? "已注册"
            : "系统占用";
    }

    private Button SmallButton(string content, string action, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = content,
            Style = (Style)FindResource("Theme.Button.Ghost"),
            Tag = action,
            Padding = new Thickness(8, 4, 8, 4),
            FontSize = 12,
            Margin = new Thickness(0, 0, 6, 0),
        };
        button.Click += handler;
        return button;
    }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action } button)
        {
            return;
        }

        if (_recordingAction == action)
        {
            CancelRecording();
            return;
        }

        CancelRecording();

        _recordingAction = action;
        _recordingScope = HotkeyCatalog.Defaults[action].Scope;
        _recordingButton = button;
        button.Content = "按键中…";
        StatusText.Text = "请按下新的组合键；Esc 取消。";
        StatusText.Foreground = (Brush)FindResource("Theme.AccentBrush");

        Focusable = true;
        Keyboard.Focus(this);
    }

    private void OnRecordingKeyDown(object sender, KeyEventArgs e)
    {
        if (_recordingAction is null)
        {
            return;
        }

        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelRecording();
            ShowStatus("已取消录制。", success: false);
            return;
        }

        if (IsModifierKey(key) || IsIgnoredKey(key))
        {
            return;
        }

        var modifiers = MapModifiers(Keyboard.Modifiers);
        if (modifiers == HotkeyModifiers.None)
        {
            ShowStatus("快捷键至少需要一个修饰键（Ctrl / Alt / Shift / Win）。", success: false);
            return;
        }

        var action = _recordingAction;
        var gesture = new HotkeyGesture(modifiers, key.ToString(), _recordingScope);
        CancelRecording();
        ApplyGesture(action, gesture);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action })
        {
            return;
        }

        CancelRecording();
        ApplyGesture(action, HotkeyCatalog.Unbound(HotkeyCatalog.Defaults[action].Scope));
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action })
        {
            return;
        }

        CancelRecording();
        _context.HotkeyBindings.Reset(action);

        var gesture = HotkeyCatalog.Defaults[action];
        if (IsRegisteredAction(action))
        {
            _context.Hotkeys.Register(action, gesture);
        }

        BuildRows();
        ShowStatus($"已恢复「{HotkeyActionCatalog.Title(action)}」默认组合键。", success: true);
    }

    private void OnResetAllClick(object sender, RoutedEventArgs e)
    {
        CancelRecording();
        _context.HotkeyBindings.ResetAll();
        foreach (var action in HotkeyRegistrationService.ActiveGlobalActions)
        {
            _context.Hotkeys.Unregister(action);
            _context.Hotkeys.Register(action, HotkeyCatalog.Defaults[action]);
        }

        BuildRows();
        ShowStatus("已恢复全部 9 项默认快捷键。", success: true);
    }

    /// <summary>
    /// 已接入系统注册的全局动作集合；模块内动作（取色 / 复制色值）只在截图模块聚焦时生效，
    /// 不进入系统注册，因此保存后只更新配置。
    /// </summary>
    private static bool IsRegisteredAction(string action) =>
        HotkeyRegistrationService.IsActiveGlobalAction(action);

    private void ApplyGesture(string action, HotkeyGesture gesture)
    {
        var previous = _context.HotkeyBindings.Get(action);
        var result = _context.HotkeyBindings.Set(action, gesture);

        if (!result.Saved)
        {
            ShowStatus(result.Message ?? "未保存。", success: false);
            return;
        }

        if (IsRegisteredAction(action))
        {
            if (HotkeyCatalog.IsUnbound(gesture))
            {
                _context.Hotkeys.Unregister(action);
                ShowStatus($"已清空「{HotkeyActionCatalog.Title(action)}」快捷键。", success: true);
            }
            else if (!_context.Hotkeys.Register(action, gesture))
            {
                if (HotkeyCatalog.IsUnbound(previous))
                {
                    _context.HotkeyBindings.Clear(action);
                }
                else
                {
                    _context.HotkeyBindings.Set(action, previous);
                }

                ShowStatus("系统拒绝该组合（可能被其他程序占用），已保留原值。", success: false);
                BuildRows();
                return;
            }
            else
            {
                ShowStatus("已保存并注册。", success: true);
            }
        }
        else if (HotkeyCatalog.IsUnbound(gesture))
        {
            ShowStatus("已清空。", success: true);
        }
        else
        {
            ShowStatus("已保存；在截图模块聚焦时生效。", success: true);
        }

        BuildRows();
    }

    private void CancelRecording()
    {
        if (_recordingButton is not null)
        {
            _recordingButton.Content = "录制";
        }

        _recordingAction = null;
        _recordingButton = null;
    }

    private void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_recordingAction is not null)
        {
            CancelRecording();
        }
    }

    private void ShowStatus(string message, bool success)
    {
        StatusText.Text = message;
        StatusText.Foreground = success
            ? (Brush)FindResource("Theme.AccentBrush")
            : (Brush)FindResource("Theme.DangerBrush");
    }

    private static HotkeyModifiers MapModifiers(ModifierKeys modifiers)
    {
        var result = HotkeyModifiers.None;

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            result |= HotkeyModifiers.Control;
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            result |= HotkeyModifiers.Alt;
        }

        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            result |= HotkeyModifiers.Shift;
        }

        if ((modifiers & ModifierKeys.Windows) != 0)
        {
            result |= HotkeyModifiers.Win;
        }

        return result;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    private static bool IsIgnoredKey(Key key) =>
        key is Key.None or Key.ImeProcessed or Key.DeadCharProcessed;
}
