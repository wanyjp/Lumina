namespace Lumina.Widgets;

/// <summary>组件视图暴露给窗口基类的模糊底层，用于抓屏后显示真实背景。</summary>
internal interface IWidgetGlassView
{
    WidgetGlassSurface GlassSurface { get; }
}
