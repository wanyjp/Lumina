using Lumina.Domain.Enums;
using Lumina.Services.Widgets;

namespace Lumina.Tests;

/// <summary>
/// 逐帧捕获的回退状态：用于总设置页在开关下方显示“为什么退回了采样模式”。
/// 静态状态跨用例共享，因此每个用例前后都要 Reset。
/// </summary>
public class WidgetGlassCaptureStatusTests
{
    [Fact]
    public void Fallback_reason_is_reported_and_cleared_when_capture_recovers()
    {
        WidgetGlassCaptureStatus.Reset();
        var changes = 0;
        void OnChanged(object? sender, EventArgs e) => changes++;
        WidgetGlassCaptureStatus.Changed += OnChanged;

        try
        {
            WidgetGlassCaptureStatus.ReportFallback(WidgetKind.Countdown, "系统不支持 Windows 图形捕获");
            Assert.Equal("系统不支持 Windows 图形捕获", WidgetGlassCaptureStatus.FallbackReason);
            Assert.Equal(1, changes);

            WidgetGlassCaptureStatus.ReportActive(WidgetKind.Countdown);
            Assert.Null(WidgetGlassCaptureStatus.FallbackReason);
            Assert.Equal(2, changes);
        }
        finally
        {
            WidgetGlassCaptureStatus.Changed -= OnChanged;
            WidgetGlassCaptureStatus.Reset();
        }
    }

    [Fact]
    public void Repeated_fallback_with_same_reason_does_not_raise_twice()
    {
        WidgetGlassCaptureStatus.Reset();
        var changes = 0;
        void OnChanged(object? sender, EventArgs e) => changes++;
        WidgetGlassCaptureStatus.Changed += OnChanged;

        try
        {
            WidgetGlassCaptureStatus.ReportFallback(WidgetKind.Clock, "无法创建显示器捕获项");
            WidgetGlassCaptureStatus.ReportFallback(WidgetKind.Clock, "无法创建显示器捕获项");
            Assert.Equal(1, changes);
        }
        finally
        {
            WidgetGlassCaptureStatus.Changed -= OnChanged;
            WidgetGlassCaptureStatus.Reset();
        }
    }

    [Fact]
    public void Multiple_widgets_keep_distinct_reasons_and_clear_independently()
    {
        WidgetGlassCaptureStatus.Reset();

        try
        {
            WidgetGlassCaptureStatus.ReportFallback(WidgetKind.Clock, "创建 D3D11 捕获设备失败");
            WidgetGlassCaptureStatus.ReportFallback(WidgetKind.Countdown, "创建 D3D11 捕获设备失败");
            Assert.Equal("创建 D3D11 捕获设备失败", WidgetGlassCaptureStatus.FallbackReason);

            WidgetGlassCaptureStatus.ReportInactive(WidgetKind.Clock);
            Assert.Equal("创建 D3D11 捕获设备失败", WidgetGlassCaptureStatus.FallbackReason);

            WidgetGlassCaptureStatus.ReportInactive(WidgetKind.Countdown);
            Assert.Null(WidgetGlassCaptureStatus.FallbackReason);
        }
        finally
        {
            WidgetGlassCaptureStatus.Reset();
        }
    }
}
