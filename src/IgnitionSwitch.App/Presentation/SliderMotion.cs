using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI.ViewManagement;

namespace IgnitionSwitch.App.Presentation;

internal sealed partial class SliderMotion : IDisposable
{
    private static readonly string[] AnimatedProperties = ["ThumbX", "FillX", "FillWidth"];
    private readonly Slider slider;
    private readonly UISettings settings = new();
    private readonly List<CompositionObject> owned = [];
    private readonly Dictionary<string, ScalarKeyFrameAnimation> animations = [];
    private readonly PointerEventHandler pressed;
    private readonly PointerEventHandler released;
    private readonly KeyEventHandler keyDown;
    private readonly KeyEventHandler keyUp;
    private Thumb? thumb;
    private Rectangle? fill;
    private Rectangle? track;
    private Visual? thumbVisual;
    private Visual? previousChild;
    private ShapeVisual? fillVisual;
    private CompositionColorBrush? fillBrush;
    private CompositionPropertySet? motion;
    private CubicBezierEasingFunction? easing;
    private Vector3 previousTranslation;
    private Vector3 target = new(float.NaN);
    private double previousOpacity;
    private bool settingValue;
    private bool pending;
    private bool animatePending;
    private bool pointerActive;
    private bool keyboardActive;
    private bool disposed;

    public SliderMotion(Slider slider)
    {
        this.slider = slider;
        pressed = (_, _) =>
        {
            pointerActive = true;
            Settle();
        };
        released = (_, _) =>
        {
            pointerActive = false;
            Settle();
        };
        keyDown = (_, args) =>
        {
            if (IsValueKey(args.Key))
            {
                keyboardActive = true;
                Settle();
            }
        };
        keyUp = (_, args) =>
        {
            if (IsValueKey(args.Key))
            {
                keyboardActive = false;
                Settle();
            }
        };
        slider.AddHandler(UIElement.PointerPressedEvent, pressed, true);
        slider.AddHandler(UIElement.PointerReleasedEvent, released, true);
        slider.AddHandler(UIElement.KeyDownEvent, keyDown, true);
        slider.AddHandler(UIElement.KeyUpEvent, keyUp, true);
        slider.PointerCaptureLost += OnPointerCaptureLost;
        slider.LostFocus += OnLostFocus;
        slider.Loaded += OnLoaded;
        slider.Unloaded += OnUnloaded;
        slider.SizeChanged += OnSizeChanged;
        slider.ValueChanged += OnValueChanged;
        slider.LayoutUpdated += OnLayoutUpdated;
        settings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;

        if (slider.IsLoaded)
        {
            Attach();
        }
    }

    public void SetValue(double value, bool animate = true)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        value = Math.Clamp(value, slider.Minimum, slider.Maximum);

        if (slider.Value == value)
        {
            if (!animate)
            {
                Settle();
            }

            return;
        }

        animatePending = animate && !pointerActive && !keyboardActive;
        settingValue = true;

        try
        {
            slider.Value = value;
        }

        finally
        {
            settingValue = false;
        }

        pending = true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        slider.RemoveHandler(UIElement.PointerPressedEvent, pressed);
        slider.RemoveHandler(UIElement.PointerReleasedEvent, released);
        slider.RemoveHandler(UIElement.KeyDownEvent, keyDown);
        slider.RemoveHandler(UIElement.KeyUpEvent, keyUp);
        slider.PointerCaptureLost -= OnPointerCaptureLost;
        slider.LostFocus -= OnLostFocus;
        slider.Loaded -= OnLoaded;
        slider.Unloaded -= OnUnloaded;
        slider.SizeChanged -= OnSizeChanged;
        slider.ValueChanged -= OnValueChanged;
        slider.LayoutUpdated -= OnLayoutUpdated;
        settings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        Detach();
    }

    private void Attach()
    {
        if (disposed || motion is not null || slider.Orientation != Orientation.Horizontal)
        {
            return;
        }

        _ = slider.ApplyTemplate();
        thumb = Find<Thumb>(slider, "HorizontalThumb");
        fill = Find<Rectangle>(slider, "HorizontalDecreaseRect");
        track = Find<Rectangle>(slider, "HorizontalTrackRect");

        if (thumb is null || fill is null || track is null)
        {
            return;
        }

        thumbVisual = ElementCompositionPreview.GetElementVisual(thumb);
        Compositor compositor = thumbVisual.Compositor;
        previousTranslation = thumb.Translation;
        previousChild = ElementCompositionPreview.GetElementChildVisual(track);
        previousOpacity = fill.Opacity;
        motion = Own(compositor.CreatePropertySet());

        foreach (string property in AnimatedProperties)
        {
            motion.InsertScalar(property, 0);
        }

        motion.InsertScalar("LayoutX", 0);
        motion.InsertScalar("TrackHeight", 0);
        easing = Own(
            compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.2f, 0.8f),
                new Vector2(0.2f, 1)
            )
        );

        foreach (string property in AnimatedProperties)
        {
            ScalarKeyFrameAnimation animation = Own(compositor.CreateScalarKeyFrameAnimation());
            animation.Target = property;
            animation.Duration = TimeSpan.FromMilliseconds(100);
            animation.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
            animation.InsertExpressionKeyFrame(0, "this.StartingValue");
            animation.InsertKeyFrame(1, 0, easing);
            animations[property] = animation;
        }

        ElementCompositionPreview.SetIsTranslationEnabled(thumb, true);
        thumbVisual.Properties.StartAnimation(
            "Translation",
            Expression(compositor, "Vector3(motion.ThumbX - motion.LayoutX, 0, 0)")
        );
        CompositionRoundedRectangleGeometry geometry = Own(
            compositor.CreateRoundedRectangleGeometry()
        );
        geometry.CornerRadius = new Vector2(2);
        geometry.StartAnimation(
            "Size",
            Expression(compositor, "Vector2(Max(0, motion.FillWidth), motion.TrackHeight)")
        );
        CompositionSpriteShape shape = Own(compositor.CreateSpriteShape(geometry));
        fillBrush = Own(compositor.CreateColorBrush());
        shape.FillBrush = fillBrush;
        shape.StartAnimation("Offset", Expression(compositor, "Vector2(motion.FillX, 0)"));
        fillVisual = Own(compositor.CreateShapeVisual());
        fillVisual.Clip = Own(compositor.CreateInsetClip());
        fillVisual.Shapes.Add(shape);
        ElementCompositionPreview.SetElementChildVisual(track, fillVisual);
        fill.Opacity = 0;
        Commit(false);
    }

    private void Commit(bool animate)
    {
        if (
            motion is null
                || thumb is null
                || fill is null
                || track is null
                || fillVisual is null || fillBrush is null
        )
        {
            return;
        }

        float trackWidth = (float)track.ActualWidth;
        float trackHeight = (float)track.ActualHeight;
        Point fillOrigin = fill
            .TransformToVisual(track)
            .TransformPoint(new Point());
        var next = new Vector3(thumb.ActualOffset.X, (float)fillOrigin.X, (float)fill.ActualWidth);
        bool smooth = animate
            && settings.AnimationsEnabled
            && !pointerActive
            && !keyboardActive
            && float.IsFinite(target.X);

        if (!smooth)
        {
            StopMotion();
        }

        motion.InsertScalar("LayoutX", next.X);
        motion.InsertScalar("TrackHeight", trackHeight);
        fillVisual.Size = new Vector2(trackWidth, trackHeight);
        fillBrush.Color = (fill.Fill as SolidColorBrush
            ?? slider.Foreground as SolidColorBrush)?.Color
            ?? Windows.UI.Color.FromArgb(255, 255, 176, 92);

        if (!smooth || next != target)
        {
            SetPosition("ThumbX", next.X, smooth);
            SetPosition("FillX", next.Y, smooth);
            SetPosition("FillWidth", next.Z, smooth);
        }

        target = next;
    }

    private void Settle()
    {
        StopMotion();
        pending = true;
        animatePending = false;
    }

    private void StopMotion()
    {
        if (motion is null)
        {
            return;
        }

        foreach (string property in AnimatedProperties)
        {
            motion.StopAnimation(property);
        }

        if (float.IsFinite(target.X))
        {
            motion.InsertScalar("ThumbX", target.X);
            motion.InsertScalar("FillX", target.Y);
            motion.InsertScalar("FillWidth", target.Z);
        }
    }

    private void SetPosition(string property, float value, bool animate)
    {
        if (animate)
        {
            ScalarKeyFrameAnimation animation = animations[property];
            animation.InsertKeyFrame(1, value, easing);
            motion!.StartAnimation(property, animation);
        }

        else
        {
            motion!.InsertScalar(property, value);
        }
    }

    private void Detach()
    {
        StopMotion();

        if (thumbVisual is not null && thumb is not null)
        {
            thumbVisual.Properties.StopAnimation("Translation");
            thumbVisual.Properties.InsertVector3("Translation", previousTranslation);
            thumb.Translation = previousTranslation;
        }

        if (fill is not null && motion is not null)
        {
            fill.Opacity = previousOpacity;
        }

        if (track is not null && motion is not null)
        {
            ElementCompositionPreview.SetElementChildVisual(track, previousChild);
        }

        for (int index = owned.Count - 1; index >= 0; index--)
        {
            owned[index].Dispose();
        }

        owned.Clear();
        motion = null;
        animations.Clear();
        thumb = null;
        fill = null;
        track = null;
        thumbVisual = null;
        fillVisual = null;
        fillBrush = null;
        easing = null;
        target = new Vector3(float.NaN);
        pointerActive = false;
        keyboardActive = false;
    }

    private ExpressionAnimation Expression(Compositor compositor, string expression)
    {
        ExpressionAnimation animation = Own(compositor.CreateExpressionAnimation(expression));
        animation.SetReferenceParameter("motion", motion);
        return animation;
    }

    private T Own<T>(T value) where T : CompositionObject
    {
        owned.Add(value);
        return value;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Attach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        Detach();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        Settle();
    }

    private void OnValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (!settingValue)
        {
            animatePending = !pointerActive && !keyboardActive;
        }

        pending = true;
    }

    private void OnLayoutUpdated(object? sender, object args)
    {
        if (pending && !disposed)
        {
            pending = false;
            Commit(animatePending);
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        pointerActive = false;
        Settle();
    }

    private void OnLostFocus(object sender, RoutedEventArgs args)
    {
        keyboardActive = false;
        Settle();
    }

    private void OnAnimationsEnabledChanged(
        UISettings sender,
        UISettingsAnimationsEnabledChangedEventArgs args
    )
    {
        _ = slider.DispatcherQueue.TryEnqueue(() =>
        {
            if (!disposed)
            {
                Settle();
                Commit(false);
            }
        });
    }

    private static bool IsValueKey(VirtualKey key)
    {
        return key is VirtualKey.Left
            or VirtualKey.Right
            or VirtualKey.Up
            or VirtualKey.Down
            or VirtualKey.Home
            or VirtualKey.End
            or VirtualKey.PageUp
            or VirtualKey.PageDown;
    }

    private static T? Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);

            if (child is T match && match.Name == name)
            {
                return match;
            }

            T? nested = Find<T>(child, name);

            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
