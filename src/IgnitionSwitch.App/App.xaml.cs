using Microsoft.UI.Xaml;

namespace IgnitionSwitch.App;

/// <summary>The native Windows application entry point.</summary>
public partial class App : Application
{
    private Window? window;

    /// <summary>Loads application resources.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new Views.MainWindow();
        window.Activate();
    }
}
