namespace Lumina.Services.Clipboard;

/// <summary>
/// 剪贴板隐私默认策略：敏感格式、密码管理器进程与常见一次性验证码/访问令牌默认不采集。
/// P2-02 的 <see cref="IgnoreRuleService"/> 与验证码开关在此基础上提供可配置能力。
/// </summary>
public static class ClipboardPrivacyFilter
{
    /// <summary>应用声明“不要记录”的剪贴板格式。</summary>
    public const string ClipboardViewerIgnoreFormat = "Clipboard Viewer Ignore";

    /// <summary>Windows 历史/云同步排除格式。</summary>
    public const string ExcludeFromMonitorFormat = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>值为 0 表示来源应用不允许写入剪贴板历史。</summary>
    public const string CanIncludeInHistoryFormat = "CanIncludeInClipboardHistory";

    /// <summary>值为 0 表示来源应用不允许上传云端剪贴板。</summary>
    public const string CanUploadToCloudFormat = "CanUploadToCloudClipboard";

    private static readonly string[] SecretFormats =
    [
        ClipboardViewerIgnoreFormat,
        ExcludeFromMonitorFormat,
    ];

    private static readonly string[] PasswordManagerProcesses =
    [
        "1password.exe",
        "keepassxc.exe",
        "bitwarden.exe",
        "lastpass.exe",
        "dashlane.exe",
    ];

    /// <summary>P3-09：保险库窗口标题特征，命中即视为密码管理器来源，默认不采集。</summary>
    private static readonly string[] VaultWindowTitles =
    [
        "密码生成器",
        "密码条目",
        "新增条目",
        "编辑条目",
        "保险库",
    ];

    /// <summary>格式名是否属于敏感/禁止记录格式（不区分大小写）。</summary>
    public static bool IsSecretFormat(string? formatName)
    {
        if (string.IsNullOrWhiteSpace(formatName))
        {
            return false;
        }

        return SecretFormats.Any(name =>
            string.Equals(name, formatName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>来源进程是否属于默认拦截的密码管理器。</summary>
    public static bool IsPasswordManagerProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        var name = processName.Trim();
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name += ".exe";
        }

        return PasswordManagerProcesses.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>来源窗口是否属于保险库相关窗口（P3-09 默认忽略规则）。</summary>
    public static bool IsVaultWindow(string? windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return false;
        }

        return VaultWindowTitles.Any(title =>
            windowTitle.Contains(title, StringComparison.Ordinal));
    }

    /// <summary>常见一次性验证码/校验码的上下文关键词。</summary>
    private static readonly string[] CodeKeywords =
    [
        "验证码",
        "校验码",
        "动态码",
        "确认码",
        "verification code",
        "one-time password",
        "one time code",
        "otp",
    ];

    /// <summary>常见访问令牌前缀；命中且长度足够时视为敏感内容。</summary>
    private static readonly string[] TokenPrefixes =
    [
        "sk-",
        "ghp_",
        "gho_",
        "ghu_",
        "ghs_",
        "ghr_",
        "github_pat_",
        "xoxb-",
        "xoxp-",
        "xoxa-",
        "glpat-",
    ];

    /// <summary>纯数字（允许常见分隔符与全角数字）的 4–8 位视为一次性验证码。</summary>
    public static bool LooksLikeOneTimeCode(string? normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return false;
        }

        var trimmed = normalizedText.Trim();
        if (IsShortDigits(trimmed))
        {
            return true;
        }

        // 123-456 / 123 456 / 123.456 这类分隔写法。
        var compact = new string(trimmed.Where(c => c is not (' ' or '-' or '.' or '\u00A0')).ToArray());
        if (compact.Length != trimmed.Length && IsShortDigits(compact))
        {
            return true;
        }

        // 全角数字。
        var halfWidth = NormalizeFullWidthDigits(trimmed);
        if (halfWidth != trimmed && IsShortDigits(halfWidth))
        {
            return true;
        }

        // “验证码：123456”这类带上下文提示的文本。
        return ContainsAnchoredCode(trimmed);
    }

    /// <summary>是否像常见访问令牌（JWT / 云厂商与服务前缀）。</summary>
    public static bool LooksLikeAccessToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length < 16)
        {
            return false;
        }

        // JWT：三段 base64url，常见开头为 eyJ。
        if (trimmed.StartsWith("eyJ", StringComparison.Ordinal)
            && trimmed.Count(c => c == '.') == 2
            && trimmed.Length >= 40)
        {
            return true;
        }

        if (trimmed.StartsWith("AKIA", StringComparison.Ordinal) && trimmed.Length == 20)
        {
            return true;
        }

        return TokenPrefixes.Any(prefix =>
            trimmed.StartsWith(prefix, StringComparison.Ordinal)
            && trimmed.Length >= prefix.Length + 8);
    }

    /// <summary>文本是否应在写入前拦截。</summary>
    public static bool IsSensitiveText(string? text) =>
        LooksLikeOneTimeCode(text) || LooksLikeAccessToken(text);

    private static bool IsShortDigits(string value) =>
        value.Length is >= 4 and <= 8 && value.All(char.IsAsciiDigit);

    private static string NormalizeFullWidthDigits(string value) =>
        new(value.Select(c => c is >= '０' and <= '９' ? (char)('0' + (c - '０')) : c).ToArray());

    private static bool ContainsAnchoredCode(string text)
    {
        if (!CodeKeywords.Any(keyword =>
                text.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // 关键词附近存在独立的 4–8 位数字组。
        var index = 0;
        while (index < text.Length)
        {
            if (!char.IsAsciiDigit(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                index++;
            }

            var length = index - start;
            var leftBounded = start == 0 || !char.IsAsciiDigit(text[start - 1]);
            var rightBounded = index >= text.Length || !char.IsAsciiDigit(text[index]);
            if (length is >= 4 and <= 8 && leftBounded && rightBounded)
            {
                return true;
            }
        }

        return false;
    }
}
