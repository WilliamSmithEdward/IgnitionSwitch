using IgnitionSwitch.App.Services;
using IgnitionSwitch.Core.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace IgnitionSwitch.App.Presentation;

internal sealed partial class SoundInspector : IDisposable
{
    private readonly StudioSession session;
    private readonly Flyout flyout;
    private readonly ScrollViewer scroll;
    private readonly TextBlock title;
    private readonly ComboBox selector;
    private readonly ToggleButton locked;
    private readonly FrameworkElement owner;
    private readonly SoundParameterControls[] rows = new SoundParameterControls[8];
    private SoundControlPage? displayed;
    private bool open;
    private bool refreshing;
    private bool disposed;

    public SoundInspector(
        StudioSession session,
        Flyout flyout,
        ScrollViewer scroll,
        TextBlock title,
        ComboBox selector,
        ToggleButton locked,
        StackPanel panel,
        FrameworkElement owner
    )
    {
        this.session = session;
        this.flyout = flyout;
        this.scroll = scroll;
        this.title = title;
        this.selector = selector;
        this.locked = locked;
        this.owner = owner;

        for (int index = 0; index < rows.Length; index++)
        {
            rows[index] = new SoundParameterControls(
                WriteParameter,
                FormatParameter,
                CompleteGesture
            );
            panel.Children.Add(rows[index].Root);
        }

        flyout.Opening += OnOpening;
        flyout.Closed += OnClosed;
        selector.SelectionChanged += OnSelectionChanged;
        locked.Click += OnLockClick;
    }

    public void Refresh()
    {
        if (!open || disposed || refreshing)
        {
            return;
        }

        refreshing = true;

        try
        {
            FlyoutViewport.Update(scroll, owner);

            SoundControlPage page = session.GetSoundControlPage();
            bool sameTarget = SameTarget(page);
            bool interacting = rows.Any(row => row.IsInteracting);

            if (!sameTarget && interacting)
            {
                CancelGestures();
                return;
            }

            bool enabled = !session.IsInteractionBlocked;
            selector.IsEnabled = enabled;
            locked.IsEnabled = enabled;
            selector.SelectedIndex = page.Device switch
            {
                SoundDeviceKind.Instrument => 0,
                SoundDeviceKind.Delay => 1,
                SoundDeviceKind.Reverb => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(page))
            };
            locked.IsChecked = page.Locked;
            locked.Content = page.Locked ? "Locked" : "Lock";
            AutomationProperties.SetName(
                locked,
                page.Locked ? "Unlock sound target" : "Lock sound target"
            );
            title.Text = page.Name;
            ToolTipService.SetToolTip(title, page.Name);

            for (int index = 0; index < rows.Length; index++)
            {
                SoundParameterControls row = rows[index];
                row.CompleteIdleGesture();
                row.Root.Visibility = index < page.Parameters.Count
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                if (index < page.Parameters.Count)
                {
                    row.Refresh(page.Parameters[index], sameTarget, enabled);
                }
            }

            displayed = page;
        }

        finally
        {
            refreshing = false;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        open = false;
        flyout.Opening -= OnOpening;
        flyout.Closed -= OnClosed;
        selector.SelectionChanged -= OnSelectionChanged;
        locked.Click -= OnLockClick;

        foreach (SoundParameterControls row in rows)
        {
            row.Dispose();
        }

        session.CompleteParameterGesture();
    }

    private bool SameTarget(SoundControlPage page)
    {
        if (
            displayed is null
                || displayed.Device != page.Device
                || displayed.Locked != page.Locked
                || displayed.Name != page.Name
                || displayed.Parameters.Count != page.Parameters.Count
        )
        {
            return false;
        }

        for (int index = 0; index < page.Parameters.Count; index++)
        {
            if (displayed.Parameters[index].Address != page.Parameters[index].Address)
            {
                return false;
            }
        }

        return true;
    }

    private bool WriteParameter(ParameterAddress address, double value)
    {
        if (!SameTarget(session.GetSoundControlPage()))
        {
            CancelGestures();
            return false;
        }

        return session.SetParameter(address, value);
    }

    private string FormatParameter(ParameterAddress address, double value)
    {
        return session.Engine
            .GetDescriptor(address)
            .Format(value);
    }

    private void CompleteGesture()
    {
        session.CompleteParameterGesture();
        Refresh();
    }

    private void CancelGestures()
    {
        bool previous = refreshing;
        refreshing = true;

        try
        {
            foreach (SoundParameterControls row in rows)
            {
                row.CancelGesture();
            }
        }

        finally
        {
            refreshing = previous;
        }
    }

    private void OnOpening(object? sender, object args)
    {
        open = true;
        Refresh();
    }

    private void OnClosed(object? sender, object args)
    {
        open = false;

        foreach (SoundParameterControls row in rows)
        {
            row.FinishInteraction();
        }

        session.CompleteParameterGesture();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (refreshing || disposed || !open)
        {
            return;
        }

        CancelGestures();
        session.SelectSoundDevice(selector.SelectedIndex switch
        {
            1 => SoundDeviceKind.Delay,
            2 => SoundDeviceKind.Reverb,
            _ => SoundDeviceKind.Instrument
        });
        Refresh();
    }

    private void OnLockClick(object sender, RoutedEventArgs args)
    {
        if (!refreshing && !disposed && open)
        {
            CancelGestures();
            session.ToggleSoundDeviceLock();
            Refresh();
        }
    }
}
