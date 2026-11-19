using Lumina.Services.Widgets;

namespace Lumina.Tests;

/// <summary>
/// P3-04 玻璃层采样节拍：静止 400ms、活动 33ms、活动后 1.2 秒回落，
/// 背景帧比对（NotifyActivity）与重置也要生效。
/// </summary>
public class GlassSamplingPolicyTests
{
    [Fact]
    public void Starts_active_then_falls_back_to_idle()
    {
        var policy = new GlassSamplingPolicy();

        Assert.Equal(GlassSamplingPolicy.ActiveIntervalMs, policy.Advance(0, activity: false));
        Assert.True(policy.IsActive);
        Assert.Equal(GlassSamplingPolicy.ActiveIntervalMs, policy.Advance(1199, activity: false));
        Assert.Equal(GlassSamplingPolicy.IdleIntervalMs, policy.Advance(1200, activity: false));
        Assert.False(policy.IsActive);
    }

    [Fact]
    public void Activity_keeps_the_high_frequency_window_open()
    {
        var policy = new GlassSamplingPolicy();
        policy.Advance(0, activity: false);

        Assert.Equal(GlassSamplingPolicy.ActiveIntervalMs, policy.Advance(600, activity: true));
        Assert.Equal(GlassSamplingPolicy.ActiveIntervalMs, policy.Advance(1700, activity: false));
        Assert.Equal(GlassSamplingPolicy.IdleIntervalMs, policy.Advance(1801, activity: false));
    }

    [Fact]
    public void Backdrop_change_notification_extends_the_window()
    {
        var policy = new GlassSamplingPolicy();
        policy.Advance(0, activity: false);

        policy.NotifyActivity(1000);
        Assert.Equal(GlassSamplingPolicy.ActiveIntervalMs, policy.Advance(2100, activity: false));
        Assert.Equal(GlassSamplingPolicy.IdleIntervalMs, policy.Advance(2201, activity: false));
    }

    [Fact]
    public void Reset_returns_to_idle_and_reheats_on_next_advance()
    {
        var policy = new GlassSamplingPolicy();
        policy.Advance(0, activity: true);

        policy.Reset();
        Assert.Equal(GlassSamplingPolicy.IdleIntervalMs, policy.CurrentIntervalMs);
        Assert.False(policy.IsActive);
        Assert.Equal(GlassSamplingPolicy.ActiveIntervalMs, policy.Advance(5000, activity: false));
    }
}
