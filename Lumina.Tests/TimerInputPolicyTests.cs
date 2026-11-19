using Lumina.Services.Clock;

namespace Lumina.Tests;

/// <summary>计时器 / 番茄钟时长输入归一化与循环次数边界。</summary>
public class TimerInputPolicyTests
{
    [Theory]
    [InlineData("0", "75", "0", 4500)]
    [InlineData("0", "0", "90", 90)]
    [InlineData("1", "15", "0", 4500)]
    [InlineData("", "25", "", 1500)]
    [InlineData("2", "0", "30", 7230)]
    public void TryParse_normalizes_overflow_into_hours(
        string hours,
        string minutes,
        string seconds,
        int expectedSeconds)
    {
        Assert.True(TimerInputPolicy.TryParse(hours, minutes, seconds, out var target));
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), target);
    }

    [Theory]
    [InlineData("0", "0", "0")]
    [InlineData("", "", "")]
    [InlineData("-1", "0", "0")]
    [InlineData("abc", "0", "0")]
    [InlineData("0", "x", "0")]
    public void TryParse_rejects_invalid_or_zero_duration(
        string hours,
        string minutes,
        string seconds)
    {
        Assert.False(TimerInputPolicy.TryParse(hours, minutes, seconds, out _));
    }

    [Fact]
    public void Split_roundtrips_normalized_duration()
    {
        var (hours, minutes, seconds) = TimerInputPolicy.Split(TimeSpan.FromSeconds(4500));
        Assert.Equal(1, hours);
        Assert.Equal(15, minutes);
        Assert.Equal(0, seconds);
    }

    [Theory]
    [InlineData("", 4)]
    [InlineData("2", 2)]
    [InlineData("12", 12)]
    [InlineData("0", 1)]
    [InlineData("20", 12)]
    [InlineData("abc", 4)]
    public void ParseCycles_clamps_to_one_through_twelve(string text, int expected)
    {
        Assert.Equal(expected, TimerInputPolicy.ParseCycles(text));
    }
}
