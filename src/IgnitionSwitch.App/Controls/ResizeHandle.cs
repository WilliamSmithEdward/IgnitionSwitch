using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace IgnitionSwitch.App.Controls;

/// <summary>A focusable pane handle supporting pointer dragging and arrow-key resizing.</summary>
public sealed partial class ResizeHandle : Control
{
    /// <summary>Identifies the axis along which this handle moves.</summary>
    public static readonly DependencyProperty AxisProperty = DependencyProperty.Register(
        nameof(Axis),
        typeof(Orientation),
        typeof(ResizeHandle),
        new PropertyMetadata(Orientation.Horizontal, OnAxisChanged)
    );
    private InputSystemCursor? cursor;
    private Thumb? thumb;
    private bool pointerOver;
    private bool dragging;

    /// <summary>Creates a resize handle with an eight-DIP keyboard step.</summary>
    public ResizeHandle()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        PointerEntered += (_, _) =>
        {
            pointerOver = true;
            UpdateVisualState();
        };
        PointerExited += (_, _) =>
        {
            pointerOver = false;
            UpdateVisualState();
        };
        GotFocus += (_, _) => UpdateVisualState();
        LostFocus += (_, _) => UpdateVisualState();
        IsEnabledChanged += (_, _) => UpdateVisualState();
        Loaded += (_, _) => UpdateCursor();
        Unloaded += (_, _) =>
        {
            ProtectedCursor = null;
            cursor?.Dispose();
            cursor = null;
        };
    }

    /// <summary>Raised with the requested horizontal or vertical change in device-independent pixels.</summary>
    public event EventHandler<double>? ResizeRequested;

    /// <summary>Gets or sets the movement axis; horizontal movement changes column widths.</summary>
    public Orientation Axis
    {
        get => (Orientation)GetValue(AxisProperty);
        set => SetValue(AxisProperty, value);
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (thumb is not null)
        {
            thumb.DragDelta -= OnDragDelta;
            thumb.DragStarted -= OnDragStarted;
            thumb.DragCompleted -= OnDragCompleted;
        }

        thumb = GetTemplateChild("DragThumb") as Thumb;

        if (thumb is not null)
        {
            thumb.DragDelta += OnDragDelta;
            thumb.DragStarted += OnDragStarted;
            thumb.DragCompleted += OnDragCompleted;
        }

        UpdateGrip();
        UpdateVisualState();
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer()
    {
        return new ResizeHandleAutomationPeer(this);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        double change = (Axis, e.Key) switch
        {
            (Orientation.Horizontal, VirtualKey.Left) or (Orientation.Vertical, VirtualKey.Up) =>
                -8,
            (Orientation.Horizontal, VirtualKey.Right) or (Orientation.Vertical, VirtualKey.Down) =>
                8,
            _ => 0
        };

        if (change == 0)
        {
            base.OnKeyDown(e);
            return;
        }

        RequestResize(change);
        e.Handled = true;
    }

    private static void OnAxisChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs args
    )
    {
        var handle = (ResizeHandle)sender;
        handle.UpdateCursor();
        handle.UpdateGrip();
    }

    private void UpdateCursor()
    {
        InputSystemCursor? previous = cursor;
        cursor = InputSystemCursor.Create(
            Axis == Orientation.Horizontal
                ? InputSystemCursorShape.SizeWestEast
                : InputSystemCursorShape.SizeNorthSouth
        );
        ProtectedCursor = cursor;
        previous?.Dispose();
    }

    private void UpdateGrip()
    {
        if (GetTemplateChild("Grip") is Border grip)
        {
            grip.Width = Axis == Orientation.Horizontal ? 3 : 40;
            grip.Height = Axis == Orientation.Horizontal ? 40 : 3;
        }
    }

    private void OnDragStarted(object sender, DragStartedEventArgs args)
    {
        dragging = true;
        _ = Focus(FocusState.Pointer);
        UpdateVisualState();
    }

    private void OnDragDelta(object sender, DragDeltaEventArgs args)
    {
        RequestResize(Axis == Orientation.Horizontal ? args.HorizontalChange : args.VerticalChange);
    }

    private void OnDragCompleted(object sender, DragCompletedEventArgs args)
    {
        dragging = false;
        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        string state = !IsEnabled
            ? "Disabled"
            : dragging
                ? "Pressed"
                : pointerOver || FocusState == FocusState.Keyboard ? "PointerOver" : "Normal";
        _ = VisualStateManager.GoToState(this, state, true);
    }

    private void RequestResize(double change)
    {
        if (double.IsFinite(change) && change != 0)
        {
            ResizeRequested?.Invoke(this, change);
        }
    }

    private sealed partial class ResizeHandleAutomationPeer(
        ResizeHandle owner
    ) : FrameworkElementAutomationPeer(owner)
    {
        protected override IList<AutomationPeer> GetChildrenCore()
        {
            return [];
        }

        protected override AutomationControlType GetAutomationControlTypeCore()
        {
            return AutomationControlType.Thumb;
        }

        protected override string GetClassNameCore()
        {
            return nameof(ResizeHandle);
        }
    }
}
