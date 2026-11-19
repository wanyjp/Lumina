namespace Lumina.Domain.Passwords;

/// <summary>密码强度结果：<see cref="Score"/> 0–4，对应界面四段强度条。</summary>
public sealed record PasswordStrengthResult(int Score, string Label)
{
    public const int MaxScore = 4;

    public int FilledSegments => Score;

    public bool IsEmpty => Score == 0;
}

/// <summary>
/// 密码强度评估：长度 + 字符类别数，输出 0–4 的稳定分数。
/// 纯计算，不依赖 UI，便于单元测试。
/// </summary>
public static class PasswordStrength
{
    private static readonly string[] Labels = ["未设置", "很弱", "较弱", "较强", "很强"];

    public static PasswordStrengthResult Evaluate(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return new PasswordStrengthResult(0, Labels[0]);
        }

        // 重复字符（aaaaaaaa / abababab）不因长度得分。
        if (password.Distinct().Count() <= 2)
        {
            return new PasswordStrengthResult(1, Labels[1]);
        }

        var classes = CountCharacterClasses(password);
        var score = 1;

        if (password.Length >= 12)
        {
            score++;
        }

        if (password.Length >= 16)
        {
            score++;
        }

        if (classes >= 3)
        {
            score++;
        }

        score = Math.Clamp(score, 1, PasswordStrengthResult.MaxScore);
        return new PasswordStrengthResult(score, Labels[score]);
    }

    public static int CountCharacterClasses(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        var lower = false;
        var upper = false;
        var digit = false;
        var symbol = false;

        foreach (var ch in password)
        {
            if (char.IsLower(ch))
            {
                lower = true;
            }
            else if (char.IsUpper(ch))
            {
                upper = true;
            }
            else if (char.IsDigit(ch))
            {
                digit = true;
            }
            else
            {
                symbol = true;
            }
        }

        return (lower ? 1 : 0) + (upper ? 1 : 0) + (digit ? 1 : 0) + (symbol ? 1 : 0);
    }
}
