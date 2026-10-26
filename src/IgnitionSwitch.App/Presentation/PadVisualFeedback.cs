using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace IgnitionSwitch.App.Presentation;

internal sealed partial class PadVisualFeedback : IDisposable
{
    private static readonly TimeSpan MinimumHit = TimeSpan.FromMilliseconds(80);
    private readonly Border host;
    private readonly UISettings settings = new();
    private readonly DispatcherTimer holdTimer = new();
    private readonly List<CompositionObject> owned = [];
    private ShapeVisual? visual;
    private CompositionRoundedRectangleGeometry? geometry;
    private ScalarKeyFrameAnimation? fade;
    private Visual? previousChild;
    private long hitTimestamp;
    private float intensity;
    private bool held;
    private bool disposed;

    public PadVisualFeedback(Border host)
    {
        this.host = host;
        host.Loaded += OnLoaded;
        host.Unloaded += OnUnloaded;
        host.SizeChanged += OnSizeChanged;
        holdTimer.Tick += OnHoldElapsed;
        settings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;

        if (host.IsLoaded)
        {
            Attach();
        }
    }

    public void Hit(int velocity)
    {
        if (disposed)
        {
            return;
        }

        if (velocity <= 0)
        {
            Release();
            return;
        }

        holdTimer.Stop();
        held = true;
        hitTimestamp = Stopwatch.GetTimestamp();
        float strength = Math.Clamp(velocity, 1, 127) / 127f;
        intensity = 0.18f + (0.42f * strength);

        if (visual is not null)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = intensity;
        }
    }

    public void Release()
    {
        if (disposed || !held)
        {
            return;
        }

        held = false;
        TimeSpan remaining = MinimumHit - Stopwatch.GetElapsedTime(hitTimestamp);

        if (remaining > TimeSpan.Zero)
        {
            holdTimer.Interval = remaining;
            holdTimer.Start();
        }

        else
        {
            Fade();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        host.Loaded -= OnLoaded;
        host.Unloaded -= OnUnloaded;
        host.SizeChanged -= OnSizeChanged;
        holdTimer.Tick -= OnHoldElapsed;
        settings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        Detach();
    }

    private void Attach()
    {
        if (disposed || visual is not null)
        {
            return;
        }

        Compositor compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        previousChild = ElementCompositionPreview.GetElementChildVisual(host);
        geometry = Own(compositor.CreateRoundedRectangleGeometry());
        geometry.CornerRadius = new Vector2(7);
        CompositionSpriteShape shape = Own(compositor.CreateSpriteShape(geometry));
        shape.FillBrush = Own(compositor.CreateColorBrush(Color.FromArgb(255, 255, 176, 92)));
        visual = Own(compositor.CreateShapeVisual());
        visual.Shapes.Add(shape);
        visual.Opacity = held ? intensity : 0;
        fade = Own(compositor.CreateScalarKeyFrameAnimation());
        fade.Duration = TimeSpan.FromMilliseconds(120);
        fade.StopBehavior = AnimationStopBehavior.SetToFinalValue;
        fade.InsertExpressionKeyFrame(0, "this.StartingValue");
        CubicBezierEasingFunction easing = Own(
            compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0), new Vector2(0.2f, 1))
        );
        fade.InsertKeyFrame(1, 0, easing);
        ElementCompositionPreview.SetElementChildVisual(host, visual);
        Resize();
    }

    private void Fade()
    {
        if (visual is null || fade is null)
        {
            return;
        }

        visual.Opacity = 0;

        if (settings.AnimationsEnabled)
        {
            fade.InsertKeyFrame(0, intensity);
            visual.StartAnimation("Opacity", fade);
        }
    }

    private void Resize()
    {
        if (visual is not null && geometry is not null)
        {
            var size = new Vector2((float)host.ActualWidth, (float)host.ActualHeight);
            visual.Size = size;
            geometry.Size = size;
        }
    }

    private void Detach()
    {
        holdTimer.Stop();
        held = false;

        if (visual is not null)
        {
            visual.StopAnimation("Opacity");
            ElementCompositionPreview.SetElementChildVisual(host, previousChild);
        }

        for (int index = owned.Count - 1; index >= 0; index--)
        {
            owned[index].Dispose();
        }

        owned.Clear();
        visual = null;
        geometry = null;
        fade = null;
        previousChild = null;
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
        Resize();
    }

    private void OnHoldElapsed(object? sender, object args)
    {
        holdTimer.Stop();
        Fade();
    }

    private void OnAnimationsEnabledChanged(
        UISettings sender,
        UISettingsAnimationsEnabledChangedEventArgs args
    )
    {
        _ = host.DispatcherQueue.TryEnqueue(() =>
        {
            if (!disposed && visual is not null && !settings.AnimationsEnabled)
            {
                visual.StopAnimation("Opacity");
                visual.Opacity = held || holdTimer.IsEnabled ? intensity : 0;
            }
        });
    }
}
