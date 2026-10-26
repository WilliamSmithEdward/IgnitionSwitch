using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace IgnitionSwitch.App.Presentation;

internal sealed partial class SoundParameterControls : IDisposable
{
    private readonly TextBlock label = new()
    {
        FontSize = 13,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly TextBlock value = new()
    {
        FontSize = 12,
        FontFamily = new FontFamily("Cascadia Mono"),
        TextAlignment = TextAlignment.Right,
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly Slider slider = new()
    {
        Minimum = 0,
        Maximum = 1,
        StepFrequency = 0.001,
        Height = 28,
        MinHeight = 28,
        IsThumbToolTipEnabled = false
    };
    private readonly SliderMotion motion;
    private readonly Func<ParameterAddress, double, bool> write;
    private readonly Func<ParameterAddress, double, string> format;
    private readonly Action completed;
    private readonly PointerEventHandler pressed;
    private readonly PointerEventHandler released;
    private readonly KeyEventHandler keyDown;
    private readonly KeyEventHandler keyUp;
    private ParameterAddress? address;
    private ParameterAddress? gestureAddress;
    private bool pointerActive;
    private bool keyboardActive;
    private bool canceled;
    private bool updating;
    private bool disposed;

    public SoundParameterControls(
        Func<ParameterAddress, double, bool> write,
        Func<ParameterAddress, double, string> format,
        Action completed
    )
    {
        this.write = write;
        this.format = format;
        this.completed = completed;
        Root.ColumnDefinitions.Add(new ColumnDefinition());
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        value.Foreground = (SolidColorBrush)Application.Current.Resources["MutedText"];
        Root.Children.Add(label);
        Grid.SetColumn(value, 1);
        Root.Children.Add(value);
        Grid.SetRow(slider, 1);
        Grid.SetColumnSpan(slider, 2);
        Root.Children.Add(slider);
        motion = new SliderMotion(slider);
        pressed = (_, _) =>
        {
            pointerActive = true;
            BeginGesture();
        };
        released = (_, _) => EndGesture();
        keyDown = (_, args) =>
        {
            if (IsValueKey(args.Key))
            {
                keyboardActive = true;
                BeginGesture();
            }
        };
        keyUp = (_, args) =>
        {
            if (IsValueKey(args.Key))
            {
                EndGesture();
            }
        };
        slider.AddHandler(UIElement.PointerPressedEvent, pressed, true);
        slider.AddHandler(UIElement.PointerReleasedEvent, released, true);
        slider.AddHandler(UIElement.KeyDownEvent, keyDown, true);
        slider.AddHandler(UIElement.KeyUpEvent, keyUp, true);
        slider.PointerCaptureLost += OnCaptureLost;
        slider.LostFocus += OnLostFocus;
        slider.ValueChanged += OnValueChanged;
    }

    public Grid Root { get; } = new()
    {
        ColumnSpacing = 12,
        RowSpacing = 2
    };
    public bool IsInteracting => pointerActive || keyboardActive;

    public void Refresh(SoundParameterSlot slot, bool animate, bool enabled)
    {
        if (!enabled)
        {
            CancelGesture();
        }

        slider.IsEnabled = enabled;

        if (IsInteracting)
        {
            return;
        }

        updating = true;

        try
        {
            address = slot.Address;
            label.Text = slot.Name;
            value.Text = slot.Value;
            AutomationProperties.SetName(slider, slot.Name);
            AutomationProperties.SetHelpText(slider, slot.Value);
            motion.SetValue(slot.Normalized, animate);
        }

        finally
        {
            updating = false;
        }
    }

    public void CompleteIdleGesture()
    {
        if (!IsInteracting && gestureAddress is not null)
        {
            EndGesture();
        }
    }

    public void CancelGesture()
    {
        canceled = IsInteracting;

        if (gestureAddress is not null)
        {
            gestureAddress = null;
            completed();
        }
    }

    public void FinishInteraction()
    {
        EndGesture();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        CancelGesture();
        slider.RemoveHandler(UIElement.PointerPressedEvent, pressed);
        slider.RemoveHandler(UIElement.PointerReleasedEvent, released);
        slider.RemoveHandler(UIElement.KeyDownEvent, keyDown);
        slider.RemoveHandler(UIElement.KeyUpEvent, keyUp);
        slider.PointerCaptureLost -= OnCaptureLost;
        slider.LostFocus -= OnLostFocus;
        slider.ValueChanged -= OnValueChanged;
        motion.Dispose();
    }

    private void BeginGesture()
    {
        if (!canceled)
        {
            gestureAddress ??= address;
        }
    }

    private void EndGesture()
    {
        pointerActive = false;
        keyboardActive = false;
        gestureAddress = null;
        canceled = false;
        completed();
    }

    private void OnValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (updating || disposed || canceled || address is null)
        {
            return;
        }

        BeginGesture();

        if (gestureAddress is { } target)
        {
            _ = write(target, args.NewValue);

            if (!canceled)
            {
                value.Text = format(target, args.NewValue);
                AutomationProperties.SetHelpText(slider, value.Text);
            }
        }
    }

    private void OnCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        EndGesture();
    }

    private void OnLostFocus(object sender, RoutedEventArgs args)
    {
        EndGesture();
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
}
