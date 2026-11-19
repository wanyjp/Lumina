using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Lumina.Domain.Enums;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;
using Forms = System.Windows.Forms;

namespace Lumina.SystemAdapters.Hotkeys;

/// <summary>
/// 全局热键：通过隐藏消息窗口接收 <c>WM_HOTKEY</c>，并在注册前做全表冲突检测。
/// 仅应在 UI 线程创建；注册失败时返回 false，由上层保留旧绑定并提示用户。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HotkeyManager : IHotkeyManager, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private const int FirstHotkeyId = 0x4000;
    private const int MaxHotkeyId = 0xBFFF;
    private const int HwndMessage = -3;

    private readonly MessageWindow _window;
    private readonly Dictionary<string, Registration> _byAction = new(StringComparer.Ordinal);
    private readonly Dictionary<int, Registration> _byId = [];

    private int _nextId = FirstHotkeyId;
    private bool _disposed;

    public HotkeyManager()
    {
        _window = new MessageWindow(OnHotkeyMessage);
    }

    public event Action<string>? Pressed;

    public bool Register(string action, HotkeyGesture gesture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(action) || !TryGetVirtualKey(gesture.Key, out var virtualKey))
        {
            return false;
        }

        var previous = _byAction.GetValueOrDefault(action);
        if (previous is not null && HotkeyConflictDetector.Matches(previous.Gesture, gesture))
        {
            return true;
        }

        var otherBindings = _byAction
            .Where(pair => pair.Key != action)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Gesture, StringComparer.Ordinal);

        if (HotkeyConflictDetector.FindConflict(otherBindings, action, gesture) is not null)
        {
            return false;
        }

        var id = previous?.Id ?? AllocateId();
        if (previous is not null)
        {
            UnregisterHotKey(_window.Handle, previous.Id);
            _byAction.Remove(action);
            _byId.Remove(previous.Id);
        }

        if (RegisterHotKey(_window.Handle, id, ToModifiers(gesture.Modifiers), virtualKey))
        {
            var registration = new Registration(action, gesture, id);
            _byAction[action] = registration;
            _byId[id] = registration;
            return true;
        }

        if (previous is not null
            && RegisterHotKey(
                _window.Handle,
                previous.Id,
                ToModifiers(previous.Gesture.Modifiers),
                GetVirtualKey(previous.Gesture.Key)))
        {
            _byAction[action] = previous;
            _byId[previous.Id] = previous;
        }

        return false;
    }

    public bool IsFree(HotkeyGesture gesture, out string? conflictAction)
    {
        foreach (var registration in _byAction.Values)
        {
            if (HotkeyConflictDetector.Matches(registration.Gesture, gesture))
            {
                conflictAction = registration.Action;
                return false;
            }
        }

        conflictAction = null;
        return true;
    }

    public void Unregister(string action)
    {
        var registration = _byAction.GetValueOrDefault(action);
        if (registration is null)
        {
            return;
        }

        UnregisterHotKey(_window.Handle, registration.Id);
        _byAction.Remove(action);
        _byId.Remove(registration.Id);
    }

    public void UnregisterAll()
    {
        foreach (var registration in _byId.Values)
        {
            UnregisterHotKey(_window.Handle, registration.Id);
        }

        _byId.Clear();
        _byAction.Clear();
    }

    private void OnHotkeyMessage(int hotkeyId)
    {
        var registration = _byId.GetValueOrDefault(hotkeyId);
        if (registration is null)
        {
            return;
        }

        try
        {
            Pressed?.Invoke(registration.Action);
        }
        catch (Exception)
        {
            // 单个订阅者异常不得中断全局消息循环。
        }
    }

    private int AllocateId()
    {
        while (_byId.ContainsKey(_nextId))
        {
            _nextId++;
            if (_nextId > MaxHotkeyId)
            {
                _nextId = FirstHotkeyId;
            }
        }

        return _nextId++;
    }

    private static uint ToModifiers(HotkeyModifiers modifiers)
    {
        var value = ModNoRepeat;
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            value |= ModControl;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            value |= ModAlt;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            value |= ModShift;
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            value |= ModWin;
        }

        return value;
    }

    private static bool TryGetVirtualKey(string key, out uint virtualKey)
    {
        if (Enum.TryParse<Forms.Keys>(key, ignoreCase: true, out var parsed) && parsed != Forms.Keys.None)
        {
            virtualKey = (uint)parsed;
            return true;
        }

        virtualKey = 0;
        return false;
    }

    private static uint GetVirtualKey(string key) =>
        TryGetVirtualKey(key, out var virtualKey) ? virtualKey : 0;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    private sealed record Registration(string Action, HotkeyGesture Gesture, int Id);

    private sealed class MessageWindow : Forms.NativeWindow
    {
        private readonly Action<int> _onHotkey;

        public MessageWindow(Action<int> onHotkey)
        {
            _onHotkey = onHotkey;
            CreateHandle(new Forms.CreateParams
            {
                Caption = "Lumina.HotkeyWindow",
                Parent = new IntPtr(HwndMessage),
            });
        }

        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == WmHotkey)
            {
                _onHotkey(message.WParam.ToInt32());
            }

            base.WndProc(ref message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        _window.DestroyHandle();
    }
}
