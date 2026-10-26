using IgnitionSwitch.App.Presentation;
using IgnitionSwitch.Core.Domain;
using IgnitionSwitch.Core.Sequencing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace IgnitionSwitch.App.Views;

public sealed partial class MainWindow
{
    private readonly TrackPlaybackState[] trackPlayback = new TrackPlaybackState[WorkstationEngine.TrackCount];
    private readonly List<Button> clipLaunchButtons = [];
    private readonly List<SceneState> displayedScenes = [];

    private Grid CreateTrackSurface(TrackControls controls, int track)
    {
        var surface = new Grid { ColumnSpacing = 4 };
        surface.ColumnDefinitions.Add(new ColumnDefinition());
        surface.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        controls.Detail.TextTrimming = TextTrimming.CharacterEllipsis;
        surface.Children.Add(controls.Button);
        var launch = new Button
        {
            Width = 32,
            Height = 36,
            Padding = new Thickness(0),
            Style = (Style)Application.Current.Resources["QuietButtonStyle"],
            Content = new FontIcon
            {
                Glyph = "\uE768",
                FontSize = 12
            },
            VerticalAlignment = VerticalAlignment.Center
        };
        launch.Click += (_, _) =>
        {
            if (SelectedSceneId() is int sceneId)
            {
                session.LaunchClip(track, sceneId);
            }

            RefreshState();
        };
        Grid.SetColumn(launch, 1);
        surface.Children.Add(launch);
        clipLaunchButtons.Add(launch);
        return surface;
    }

    private void WireSceneControls()
    {
        SceneSelector.SelectionChanged += (_, _) =>
        {
            if (!updating && SceneSelector.SelectedItem is ComboBoxItem { Tag: int sceneId })
            {
                session.SelectScene(sceneId);
                RefreshState();
            }
        };
        LaunchSceneButton.Click += (_, _) =>
        {
            if (SelectedSceneId() is int sceneId)
            {
                session.LaunchScene(sceneId);
            }

            RefreshState();
        };
        SoloButton.Click += (_, _) =>
        {
            session.ToggleTrackSolo(session.Engine.SelectedTrack);
            RefreshState();
        };
        StopClipButton.Click += (_, _) =>
        {
            session.StopClip(session.Engine.SelectedTrack);
            RefreshState();
        };
        NewSceneItem.Click += (_, _) => CreateScene(false);
        DuplicateSceneItem.Click += (_, _) => CreateScene(true);
        RenameSceneItem.Click += async (_, _) => await RenameSceneAsync();
        DeleteSceneItem.Click += async (_, _) => await DeleteSceneAsync();
        ClearClipItem.Click += async (_, _) => await ClearClipAsync();
    }

    private void UpdateClipPlayheads()
    {
        session.Engine.CopyTrackPlaybackStates(trackPlayback);
        timeline.UpdatePlayback(trackPlayback, timelineSceneId, session.Engine.IsPlaying);
    }

    private void RefreshSceneControls(IReadOnlyList<TrackState> tracks)
    {
        IReadOnlyList<SceneState> scenes = session.Engine.Scenes;

        if (!displayedScenes.SequenceEqual(scenes))
        {
            displayedScenes.Clear();
            displayedScenes.AddRange(scenes);
            SceneSelector.Items.Clear();

            foreach (SceneState scene in scenes)
            {
                SceneSelector.Items.Add(new ComboBoxItem
                {
                    Tag = scene.Id,
                    Content = new TextBlock
                    {
                        Text = scene.Name,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 240
                    }
                });
            }
        }

        int editingScene = session.Engine.EditingSceneId;
        SceneSelector.SelectedIndex = displayedScenes.FindIndex(scene => scene.Id == editingScene);
        bool enabled = !session.IsInteractionBlocked;
        SceneSelector.IsEnabled = enabled;
        LaunchSceneButton.IsEnabled = enabled && session.Audio.IsRunning;
        NewSceneItem.IsEnabled = enabled && scenes.Count < WorkstationEngine.MaximumScenes;
        DuplicateSceneItem.IsEnabled = NewSceneItem.IsEnabled;
        RenameSceneItem.IsEnabled = enabled;
        DeleteSceneItem.IsEnabled = enabled && scenes.Count > 1;
        ClearClipItem.IsEnabled = enabled
            && (session.Engine.IsRecording || tracks[session.Engine.SelectedTrack].NoteCount > 0);
        SoloButton.IsEnabled = enabled;
        SoloButton.IsChecked = session.Engine.GetTrackSolo(session.Engine.SelectedTrack);
        StopClipButton.IsEnabled = enabled;

        for (int track = 0; track < tracks.Count; track++)
        {
            TrackPlaybackState state = trackPlayback[track];
            string detail = state.StopQueued
                ? $"{SceneName(state.ActiveSceneId)} · stopping"
                : state.QueuedSceneId is int queued
                    ? $"{SceneName(queued)} · queued"
                    : state.ActiveSceneId is int active
                        ? $"{SceneName(active)} · {(session.Engine.IsPlaying ? "playing" : "ready")}"
                        : $"Stopped · {tracks[track].NoteCount} notes";

            if (session.Engine.GetTrackSolo(track))
            {
                detail = $"Solo · {detail}";
            }

            if (tracks[track].IsMuted)
            {
                detail = $"Muted · {detail}";
            }

            trackControls[track].Detail.Text = detail;
            ToolTipService.SetToolTip(trackControls[track].Detail, detail);
            AutomationProperties.SetHelpText(trackControls[track].Button, detail);
            Button launch = clipLaunchButtons[track];
            launch.IsEnabled = enabled && session.Audio.IsRunning;
            launch.Foreground = InstrumentControls.Brush(
                state.QueuedSceneId == editingScene
                    ? "#FFB05C"
                    : state.ActiveSceneId == editingScene ? "#6AD7E1" : "#9CA8BD"
            );
            string action = $"Launch {tracks[track].Name} clip from {SceneName(editingScene)}";
            AutomationProperties.SetName(launch, action);
            ToolTipService.SetToolTip(launch, action);
        }
    }

    private string SceneName(int? id)
    {
        return displayedScenes.Find(scene => scene.Id == id)?.Name ?? "Clip";
    }

    private int? SelectedSceneId()
    {
        return SceneSelector.SelectedItem is ComboBoxItem { Tag: int sceneId } ? sceneId : null;
    }

    private void CreateScene(bool duplicate)
    {
        if (disposed || session.IsInteractionBlocked)
        {
            return;
        }

        int? sceneId = duplicate
            ? SelectedSceneId() is int source ? session.DuplicateScene(source) : null
            : session.CreateScene();

        if (sceneId is int created)
        {
            session.SelectScene(created);
        }

        else
        {
            StatusText.Text = session.Engine.Scenes.Count >= WorkstationEngine.MaximumScenes
                ? $"A project can contain up to {WorkstationEngine.MaximumScenes} scenes."
                : duplicate
                    ? "This copy would exceed the project’s note limit."
                    : "Could not create the scene.";
        }

        RefreshState();
    }

    private async Task RenameSceneAsync()
    {
        if (dialogOpen || SelectedSceneId() is not int sceneId)
        {
            return;
        }

        var name = new TextBox
        {
            Text = SceneName(sceneId),
            MaxLength = 32,
            MinWidth = 260
        };
        AutomationProperties.SetName(name, "Scene name");
        var dialog = new ContentDialog
        {
            Title = "Rename scene",
            Content = name,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark
        };
        name.TextChanged += (_, _) =>
            dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(name.Text)
                && !name.Text.Any(char.IsControl);
        name.Loaded += (_, _) => name.SelectAll();
        SetInteractionBlocked(true);
        string? replacement = null;

        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                replacement = name.Text.Trim();
            }
        }

        finally
        {
            SetInteractionBlocked(false);
        }

        if (replacement is not null)
        {
            _ = session.RenameScene(sceneId, replacement);
        }

        RefreshState();
    }

    private async Task DeleteSceneAsync()
    {
        if (SelectedSceneId() is not int sceneId)
        {
            return;
        }

        if (
            await ConfirmAsync(
                "Delete this scene?",
                $"Delete “{SceneName(sceneId)}” and its clips from all four tracks?",
                "Delete scene"
            )
        )
        {
            _ = session.DeleteScene(sceneId);
            RefreshState();
        }
    }

    private async Task ClearClipAsync()
    {
        if (SelectedSceneId() is not int sceneId)
        {
            return;
        }

        int track = session.Engine.SelectedTrack;

        if (
            await ConfirmAsync(
                "Clear this clip?",
                "This removes the notes in the selected scene’s clip on this track.",
                "Clear clip"
            )
        )
        {
            if (session.ClearClip(track, sceneId))
            {
                StatusText.Text = "Clip cleared.";
            }

            RefreshState();
        }
    }
}
