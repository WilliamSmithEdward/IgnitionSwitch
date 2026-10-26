using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace IgnitionSwitch.App.Presentation;

internal sealed partial class PadControls : IDisposable
{
    private readonly Action<int, int> noteOn;
    private readonly Action<int, int> noteOff;
    private readonly PadVisualFeedback feedback;
    private readonly DispatcherTimer clickTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(180)
    };
    private readonly PointerEventHandler pressed;
    private readonly PointerEventHandler released;
    private bool ownsNote;
    private bool pointerClick;
    private bool disposed;

    public PadControls(
        int note,
        Button button,
        Border feedbackHost,
        Action<int, int> noteOn,
        Action<int, int> noteOff
    )
    {
        Note = note;
        Button = button;
        this.noteOn = noteOn;
        this.noteOff = noteOff;
        feedback = new PadVisualFeedback(feedbackHost);
        pressed = OnPointerPressed;
        released = OnPointerReleased;
        button.AddHandler(UIElement.PointerPressedEvent, pressed, true);
        button.AddHandler(UIElement.PointerReleasedEvent, released, true);
        button.PointerCaptureLost += OnPointerCaptureLost;
        button.Click += OnClick;
        button.Unloaded += OnUnloaded;
        clickTimer.Tick += OnClickTimer;
    }

    public int Note { get; }
    public Button Button { get; }

    public void Hit(int velocity)
    {
        if (!disposed)
        {
            feedback.Hit(velocity);
        }
    }

    public void Release()
    {
        if (!disposed)
        {
            feedback.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ReleaseOwnedNote();
        Button.RemoveHandler(UIElement.PointerPressedEvent, pressed);
        Button.RemoveHandler(UIElement.PointerReleasedEvent, released);
        Button.PointerCaptureLost -= OnPointerCaptureLost;
        Button.Click -= OnClick;
        Button.Unloaded -= OnUnloaded;
        clickTimer.Tick -= OnClickTimer;
        feedback.Dispose();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        if (!args.GetCurrentPoint(Button).Properties.IsLeftButtonPressed)
        {
            return;
        }

        ReleaseOwnedNote();
        pointerClick = true;
        ownsNote = true;
        noteOn(3, Note);
        _ = Button.CapturePointer(args.Pointer);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        ReleasePointer();
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs args)
    {
        ReleasePointer();
    }

    private void ReleasePointer()
    {
        ReleaseOwnedNote();
        _ = Button.DispatcherQueue.TryEnqueue(() => pointerClick = false);
    }

    private void OnClick(object sender, RoutedEventArgs args)
    {
        if (pointerClick)
        {
            pointerClick = false;
            return;
        }

        ReleaseOwnedNote();
        ownsNote = true;
        noteOn(3, Note);
        clickTimer.Start();
    }

    private void OnClickTimer(object? sender, object args)
    {
        ReleaseOwnedNote();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        ReleaseOwnedNote();
        pointerClick = false;
    }

    private void ReleaseOwnedNote()
    {
        clickTimer.Stop();

        if (ownsNote)
        {
            ownsNote = false;
            noteOff(3, Note);
        }
    }
}
