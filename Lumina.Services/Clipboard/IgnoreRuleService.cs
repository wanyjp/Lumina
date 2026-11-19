using System.Text.Json;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Clipboard;

/// <summary>
/// 剪贴板忽略规则：按前台窗口标题或进程名做大小写不敏感的子串匹配。
/// 规则以 JSON 数组保存在偏好目录；读取失败回落默认值，保存空数组表示“无自定义规则”。
/// </summary>
public sealed class IgnoreRuleService
{
    private readonly ISettingsStore _settings;
    private readonly object _gate = new();

    public IgnoreRuleService(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    /// <summary>默认规则；内置密码管理器兜底由 <see cref="ClipboardPrivacyFilter"/> 另行保证。</summary>
    public static IReadOnlyList<string> DefaultRules => ClipboardIgnoreDefaults.Rules;

    /// <summary>当前生效规则（规范化、大小写不敏感去重，保持首次出现顺序）。</summary>
    public IReadOnlyList<string> Rules
    {
        get
        {
            lock (_gate)
            {
                return LoadRules();
            }
        }
    }

    /// <summary>是否应忽略该来源窗口；标题或进程名命中任一规则即忽略。</summary>
    public bool ShouldIgnoreSource(string? windowTitle, string? processName)
    {
        var title = windowTitle?.Trim() ?? string.Empty;
        var process = processName?.Trim() ?? string.Empty;
        if (title.Length == 0 && process.Length == 0)
        {
            return false;
        }

        foreach (var rule in Rules)
        {
            if (Matches(rule, title) || Matches(rule, process))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>新增规则；空白与大小写重复返回 <c>false</c>，不改变现有规则。</summary>
    public bool AddRule(string? rule)
    {
        var normalized = Normalize(rule);
        if (normalized is null)
        {
            return false;
        }

        lock (_gate)
        {
            var current = LoadRules();
            if (current.Any(item => SameRule(item, normalized)))
            {
                return false;
            }

            SaveRules(current.Append(normalized));
            return true;
        }
    }

    /// <summary>按大小写不敏感匹配移除规则；不存在返回 <c>false</c>。</summary>
    public bool RemoveRule(string? rule)
    {
        var normalized = Normalize(rule);
        if (normalized is null)
        {
            return false;
        }

        lock (_gate)
        {
            var current = LoadRules();
            var remaining = current.Where(item => !SameRule(item, normalized)).ToArray();
            if (remaining.Length == current.Count)
            {
                return false;
            }

            SaveRules(remaining);
            return true;
        }
    }

    /// <summary>覆盖保存规则；空白丢弃，大小写不敏感去重。传入 <c>null</c> 时恢复默认。</summary>
    public IReadOnlyList<string> SaveRules(IEnumerable<string>? rules)
    {
        var normalized = NormalizeRules(rules ?? DefaultRules);
        Persist(normalized);
        return normalized;
    }

    /// <summary>恢复计划默认规则。</summary>
    public IReadOnlyList<string> ResetDefaults() => SaveRules(DefaultRules);

    /// <summary>是否把纯 4–8 位数字当作一次性验证码拦截；<c>false</c> 允许采集纯数字内容。</summary>
    public bool BlockOneTimeCodes =>
        _settings.Get(
            PreferenceKeys.ClipboardBlockOneTimeCodes,
            PreferenceDefaults.ClipboardBlockOneTimeCodes);

    public void SetBlockOneTimeCodes(bool enabled) =>
        _settings.Set(
            PreferenceKeys.ClipboardBlockOneTimeCodes,
            enabled,
            module: "clipboard");

    private IReadOnlyList<string> LoadRules()
    {
        var raw = _settings.Get<string>(
            PreferenceKeys.ClipboardIgnoreRules,
            PreferenceDefaults.ClipboardIgnoreRules);

        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultRules;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(raw);
            return parsed is null ? DefaultRules : NormalizeRules(parsed);
        }
        catch (JsonException)
        {
            return DefaultRules;
        }
    }

    private void Persist(IReadOnlyList<string> rules)
    {
        _settings.Set(
            PreferenceKeys.ClipboardIgnoreRules,
            JsonSerializer.Serialize(rules),
            module: "clipboard");
    }

    private static IReadOnlyList<string> NormalizeRules(IEnumerable<string> rules)
    {
        var normalized = new List<string>();
        foreach (var rule in rules)
        {
            var value = Normalize(rule);
            if (value is null || normalized.Any(item => SameRule(item, value)))
            {
                continue;
            }

            normalized.Add(value);
        }

        return normalized;
    }

    private static string? Normalize(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return null;
        }

        return rule.Trim();
    }

    private static bool SameRule(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string rule, string text) =>
        text.Length > 0 && text.Contains(rule, StringComparison.OrdinalIgnoreCase);
}
