using Lumina.Domain.Enums;
using Lumina.Domain.Models;
using Lumina.Services.Configuration;
using Lumina.Services.Contracts;

namespace Lumina.Services.Completion;

/// <summary>四套完成风格策略；视图只消费 <see cref="CompletionVisual"/>，不写分支。</summary>
public static class CompletionStyleCatalog
{
    private static readonly ICompletionStyleStrategy[] Strategies =
    [
        new StrokeCompletionStrategy(),
        new FillCompletionStrategy(),
        new RingCompletionStrategy(),
        new DotCompletionStrategy(),
    ];

    public static IReadOnlyList<ICompletionStyleStrategy> All => Strategies;

    public static ICompletionStyleStrategy For(CompletionStyle style) =>
        Strategies.FirstOrDefault(strategy => strategy.Style == style) ?? Strategies[0];

    public static string Key(CompletionStyle style) => style switch
    {
        CompletionStyle.Fill => "fill",
        CompletionStyle.Ring => "ring",
        CompletionStyle.Dot => "dot",
        _ => "stroke",
    };

    public static string Label(CompletionStyle style) => style switch
    {
        CompletionStyle.Fill => "填充折叠",
        CompletionStyle.Ring => "环形进度",
        CompletionStyle.Dot => "状态圆点",
        _ => "描边勾选",
    };

    public static CompletionStyle Parse(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "fill" => CompletionStyle.Fill,
        "ring" => CompletionStyle.Ring,
        "dot" => CompletionStyle.Dot,
        _ => CompletionStyle.Stroke,
    };
}

/// <summary>完成风格切换中心：读取持久化偏好，切换后通知所有视图即时重渲染。</summary>
public sealed class CompletionStyleHub
{
    private readonly ISettingsStore _settings;

    public CompletionStyleHub(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        Current = CompletionStyleCatalog.Parse(settings.Get<string>(PreferenceKeys.CheckStyle));
        ReducedMotion = settings.Get<bool>(PreferenceKeys.ReduceMotion);
    }

    public CompletionStyle Current { get; private set; }

    public bool ReducedMotion { get; private set; }

    public ICompletionStyleStrategy Strategy => CompletionStyleCatalog.For(Current);

    public event Action<CompletionStyle>? Changed;

    public event Action? MotionChanged;

    public void Set(CompletionStyle style)
    {
        if (Current == style)
        {
            return;
        }

        Current = style;
        _settings.Set(PreferenceKeys.CheckStyle, CompletionStyleCatalog.Key(style), module: "settings");
        Changed?.Invoke(style);
    }

    public void SetReducedMotion(bool reduced)
    {
        if (ReducedMotion == reduced)
        {
            return;
        }

        ReducedMotion = reduced;
        _settings.Set(PreferenceKeys.ReduceMotion, reduced, module: "settings");
        MotionChanged?.Invoke();
    }
}

/// <summary>四套策略共享的状态映射。</summary>
public abstract class CompletionStyleStrategyBase : ICompletionStyleStrategy
{
    public abstract CompletionStyle Style { get; }

    public CompletionVisual Render(bool done, bool reducedMotion) => new()
    {
        Style = Style,
        Done = done,
        ReducedMotion = reducedMotion,
    };
}

internal sealed class StrokeCompletionStrategy : CompletionStyleStrategyBase
{
    public override CompletionStyle Style => CompletionStyle.Stroke;
}

internal sealed class FillCompletionStrategy : CompletionStyleStrategyBase
{
    public override CompletionStyle Style => CompletionStyle.Fill;
}

internal sealed class RingCompletionStrategy : CompletionStyleStrategyBase
{
    public override CompletionStyle Style => CompletionStyle.Ring;
}

internal sealed class DotCompletionStrategy : CompletionStyleStrategyBase
{
    public override CompletionStyle Style => CompletionStyle.Dot;
}
