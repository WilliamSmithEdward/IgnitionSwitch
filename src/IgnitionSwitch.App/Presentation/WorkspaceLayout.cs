using IgnitionSwitch.App.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IgnitionSwitch.App.Presentation;

internal sealed partial class WorkspaceLayout : IDisposable
{
    private const double MinimumBodyHeight = 490;
    private const double MinimumTrackWidth = 200;
    private const double MaximumTrackWidth = 360;
    private const double MinimumEditorWidth = 480;
    private const double MinimumLoopHeight = 230;
    private const double MinimumDrumsHeight = 200;
    private const double HandleSize = 12;
    private readonly ScrollViewer scroll;
    private readonly Grid root;
    private readonly Grid body;
    private readonly ColumnDefinition trackColumn;
    private readonly Grid editor;
    private readonly RowDefinition loopRow;
    private readonly RowDefinition drumsRow;
    private readonly ResizeHandle trackHandle;
    private readonly ResizeHandle loopHandle;
    private double trackRatio = double.NaN;
    private double loopRatio = 0.55;
    private double trackWidth = 244;
    private double loopHeight;
    private bool queued;
    private bool disposed;

    public WorkspaceLayout(
        ScrollViewer scroll,
        Grid root,
        Grid body,
        ColumnDefinition trackColumn,
        Grid editor,
        RowDefinition loopRow,
        RowDefinition drumsRow,
        ResizeHandle trackHandle,
        ResizeHandle loopHandle
    )
    {
        this.scroll = scroll;
        this.root = root;
        this.body = body;
        this.trackColumn = trackColumn;
        this.editor = editor;
        this.loopRow = loopRow;
        this.drumsRow = drumsRow;
        this.trackHandle = trackHandle;
        this.loopHandle = loopHandle;
        scroll.Loaded += OnLoaded;
        scroll.SizeChanged += OnSizeChanged;
        root.SizeChanged += OnSizeChanged;
        body.SizeChanged += OnSizeChanged;
        editor.SizeChanged += OnSizeChanged;
        trackHandle.ResizeRequested += OnTrackResize;
        loopHandle.ResizeRequested += OnLoopResize;
        QueueRefresh();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        scroll.Loaded -= OnLoaded;
        scroll.SizeChanged -= OnSizeChanged;
        root.SizeChanged -= OnSizeChanged;
        body.SizeChanged -= OnSizeChanged;
        editor.SizeChanged -= OnSizeChanged;
        trackHandle.ResizeRequested -= OnTrackResize;
        loopHandle.ResizeRequested -= OnLoopResize;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        QueueRefresh();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs args)
    {
        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (disposed || queued)
        {
            return;
        }

        queued = true;

        if (!scroll.DispatcherQueue.TryEnqueue(() =>
            {
                queued = false;

                if (!disposed)
                {
                    Refresh();
                }
            }))
        {
            queued = false;
        }
    }

    private void Refresh()
    {
        double viewport = scroll.ViewportHeight > 0 ? scroll.ViewportHeight : scroll.ActualHeight;

        if (!double.IsFinite(viewport) || viewport <= 0)
        {
            return;
        }

        // A finite height lets star rows fill the viewport even inside a vertical ScrollViewer.
        double fixedHeight = root.Padding.Top
            + root.Padding.Bottom
            + (root.RowSpacing * (root.RowDefinitions.Count - 1));

        for (int row = 0; row < root.RowDefinitions.Count; row++)
        {
            if (row != Grid.GetRow(body))
            {
                fixedHeight += root.RowDefinitions[row].ActualHeight;
            }
        }

        double height = Math.Max(viewport, fixedHeight + MinimumBodyHeight);

        if (!double.IsFinite(root.Height) || Math.Abs(root.Height - height) > 0.1)
        {
            root.Height = height;
        }

        ApplyTrackWidth(false);
        ApplyLoopHeight(false);
    }

    private void OnTrackResize(object? sender, double change)
    {
        trackWidth += change;
        ApplyTrackWidth(true);
    }

    private void ApplyTrackWidth(bool saveRatio)
    {
        double width = body.ActualWidth;

        if (!double.IsFinite(width) || width <= HandleSize)
        {
            return;
        }

        if (!double.IsFinite(trackRatio))
        {
            trackRatio = trackWidth / width;
        }

        double minimum = Math.Min(MinimumTrackWidth, width * 0.3);
        double editorMinimum = Math.Min(
            MinimumEditorWidth,
            Math.Max(0, width - minimum - HandleSize)
        );
        double maximum = Math.Max(
            minimum,
            Math.Min(MaximumTrackWidth, width - editorMinimum - HandleSize)
        );
        double target = saveRatio ? trackWidth : width * trackRatio;
        trackWidth = Math.Clamp(target, minimum, maximum);
        trackColumn.MinWidth = Math.Min(trackColumn.MinWidth, minimum);
        trackColumn.MaxWidth = maximum;
        trackColumn.MinWidth = minimum;
        body.ColumnDefinitions[2].MinWidth = editorMinimum;

        if (Math.Abs(trackColumn.Width.Value - trackWidth) > 0.1 || !trackColumn.Width.IsAbsolute)
        {
            trackColumn.Width = new GridLength(trackWidth);
        }

        if (saveRatio)
        {
            trackRatio = trackWidth / width;
        }
    }

    private void OnLoopResize(object? sender, double change)
    {
        loopHeight += change;
        ApplyLoopHeight(true);
    }

    private void ApplyLoopHeight(bool saveRatio)
    {
        double available = editor.ActualHeight - HandleSize;

        if (!double.IsFinite(available) || available <= 0)
        {
            return;
        }

        double minimum = Math.Min(MinimumLoopHeight, available * 0.55);
        double drumsMinimum = Math.Min(MinimumDrumsHeight, available - minimum);
        double target = saveRatio ? loopHeight : available * loopRatio;
        loopHeight = Math.Clamp(target, minimum, available - drumsMinimum);
        loopRow.MinHeight = minimum;
        drumsRow.MinHeight = drumsMinimum;
        var loopLength = new GridLength(loopHeight, GridUnitType.Star);
        var drumsLength = new GridLength(available - loopHeight, GridUnitType.Star);

        if (loopRow.Height != loopLength)
        {
            loopRow.Height = loopLength;
        }

        if (drumsRow.Height != drumsLength)
        {
            drumsRow.Height = drumsLength;
        }

        if (saveRatio)
        {
            loopRatio = loopHeight / available;
        }
    }
}
