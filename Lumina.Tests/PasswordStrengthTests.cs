using Lumina.Domain.Passwords;

namespace Lumina.Tests;

/// <summary>P3-08 密码强度：长度与字符类别映射到四段分数。</summary>
public class PasswordStrengthTests
{
    [Theory]
    [InlineData(null, 0, "未设置")]
    [InlineData("", 0, "未设置")]
    [InlineData("abc", 1, "很弱")]
    [InlineData("abcdefgh", 1, "很弱")]
    [InlineData("Abcdef12", 2, "较弱")]
    [InlineData("Abcdef123456", 3, "较强")]
    [InlineData("Abcdef123456!@#$", 4, "很强")]
    public void Evaluate_maps_length_and_classes(string? password, int score, string label)
    {
        var result = PasswordStrength.Evaluate(password);

        Assert.Equal(score, result.Score);
        Assert.Equal(label, result.Label);
    }

    [Fact]
    public void Count_classes_recognizes_four_groups()
    {
        Assert.Equal(4, PasswordStrength.CountCharacterClasses("aA1!"));
        Assert.Equal(3, PasswordStrength.CountCharacterClasses("aA1"));
        Assert.Equal(1, PasswordStrength.CountCharacterClasses("abcdef"));
        Assert.Equal(1, PasswordStrength.CountCharacterClasses("密码"));
    }

    [Fact]
    public void Filled_segments_never_exceed_four()
    {
        Assert.Equal(0, PasswordStrength.Evaluate(null).FilledSegments);
        Assert.Equal(4, PasswordStrength.Evaluate("Abcdef123456!@#$%^&*").FilledSegments);
    }
}
