namespace Lumina.Services.Clipboard;

/// <summary>忽略规则默认值：默认覆盖计划要求的三个密码管理器进程。</summary>
public static class ClipboardIgnoreDefaults
{
    public static IReadOnlyList<string> Rules { get; } =
        ["1Password.exe", "KeePassXC.exe", "Bitwarden.exe"];

    /// <summary>写入偏好目录与配置文件共享的默认 JSON。</summary>
    public const string Json = """["1Password.exe","KeePassXC.exe","Bitwarden.exe"]""";
}
