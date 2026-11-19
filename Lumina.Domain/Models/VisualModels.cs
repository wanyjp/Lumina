using Lumina.Domain.Enums;

namespace Lumina.Domain.Models;

/// <summary>完成态的视觉描述；具体渲染由表现层按风格策略消费。</summary>
public sealed record CompletionVisual
{
    public required CompletionStyle Style { get; init; }

    public required bool Done { get; init; }

    public required bool ReducedMotion { get; init; }
}

/// <summary>紧迫度快照；颜色、进度与呼吸三项独立表征状态。</summary>
public sealed record UrgencySnapshot
{
    public required UrgencyLevel Level { get; init; }

    public required int DaysRemaining { get; init; }

    /// <summary>0–1 的时间消耗比例，用于底部进度条。</summary>
    public required double ProgressRatio { get; init; }

    public required bool ShouldBreathe { get; init; }

    /// <summary>下次（或原）发生日，供卡片与组件显示；日期无效时为 null。</summary>
    public DateOnly? NextOccurrence { get; init; }
}
