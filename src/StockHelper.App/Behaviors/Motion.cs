using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StockHelper.App.Behaviors;

public enum MotionKind
{
    None,

    /// <summary>Fade in while moving up a little (pages, sections, cards).</summary>
    FadeUp,

    /// <summary>Fade in while sliding from the right (side panels).</summary>
    SlideFromRight,

    /// <summary>Fade in while growing slightly (dialogs, popups).</summary>
    Pop,

    /// <summary>Grow upwards from the bottom edge (chart bars).</summary>
    Grow,
}

/// <summary>
/// Entrance animations as attached properties, so views stay declarative:
/// <c>b:Motion.OnContentChange="FadeUp"</c> on a ContentControl, <c>b:Motion.OnVisible="SlideFromRight"</c> on any element.
/// </summary>
public static class Motion
{
    private static readonly Duration Normal = new(TimeSpan.FromMilliseconds(260));
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    public static readonly DependencyProperty OnContentChangeProperty = DependencyProperty.RegisterAttached(
        "OnContentChange", typeof(MotionKind), typeof(Motion), new PropertyMetadata(MotionKind.None, OnContentChangeChanged));

    public static readonly DependencyProperty OnVisibleProperty = DependencyProperty.RegisterAttached(
        "OnVisible", typeof(MotionKind), typeof(Motion), new PropertyMetadata(MotionKind.None, OnVisibleChanged));

    public static readonly DependencyProperty OnLoadedProperty = DependencyProperty.RegisterAttached(
        "OnLoaded", typeof(MotionKind), typeof(Motion), new PropertyMetadata(MotionKind.None, OnLoadedChanged));

    public static MotionKind GetOnContentChange(DependencyObject d) => (MotionKind)d.GetValue(OnContentChangeProperty);

    public static void SetOnContentChange(DependencyObject d, MotionKind value) => d.SetValue(OnContentChangeProperty, value);

    public static MotionKind GetOnVisible(DependencyObject d) => (MotionKind)d.GetValue(OnVisibleProperty);

    public static void SetOnVisible(DependencyObject d, MotionKind value) => d.SetValue(OnVisibleProperty, value);

    public static MotionKind GetOnLoaded(DependencyObject d) => (MotionKind)d.GetValue(OnLoadedProperty);

    public static void SetOnLoaded(DependencyObject d, MotionKind value) => d.SetValue(OnLoadedProperty, value);

    /// <summary>Plays an entrance animation on the element.</summary>
    public static void Play(UIElement element, MotionKind kind)
    {
        if (kind == MotionKind.None || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        var translate = new TranslateTransform();
        var scale = new ScaleTransform(1, 1);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup { Children = { scale, translate } };

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Normal) { EasingFunction = Ease });
        switch (kind)
        {
            case MotionKind.FadeUp:
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, Normal) { EasingFunction = Ease });
                break;
            case MotionKind.SlideFromRight:
                translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(36, 0, Normal) { EasingFunction = Ease });
                break;
            case MotionKind.Grow:
                element.RenderTransformOrigin = new Point(0.5, 1);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(520))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                break;
            case MotionKind.Pop:
                var grow = new DoubleAnimation(0.96, 1, Normal) { EasingFunction = Ease };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
                break;
        }
    }

    private static void OnContentChangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ContentControl control)
        {
            return;
        }

        var descriptor = DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));
        descriptor.RemoveValueChanged(control, OnContentValueChanged);
        if ((MotionKind)e.NewValue != MotionKind.None)
        {
            descriptor.AddValueChanged(control, OnContentValueChanged);
        }
    }

    private static void OnContentValueChanged(object? sender, EventArgs e)
    {
        if (sender is ContentControl { Content: not null } control)
        {
            Play(control, GetOnContentChange(control));
        }
    }

    private static void OnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.IsVisibleChanged -= OnIsVisibleChanged;
        if ((MotionKind)e.NewValue != MotionKind.None)
        {
            element.IsVisibleChanged += OnIsVisibleChanged;
        }
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is UIElement element && e.NewValue is true)
        {
            Play(element, GetOnVisible(element));
        }
    }

    private static void OnLoadedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && (MotionKind)e.NewValue != MotionKind.None)
        {
            element.Loaded += (_, _) => Play(element, GetOnLoaded(element));
        }
    }
}
