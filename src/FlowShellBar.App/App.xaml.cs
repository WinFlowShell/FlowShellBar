using FlowShellBar.App.Host;
using FlowShellBar.App.Ui;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FlowShellBar.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private AppBootstrapper? _bootstrapper;
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        base.OnLaunched(args);

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The UI dispatcher queue is not available.");

        _bootstrapper = new AppBootstrapper();
        var runtime = await _bootstrapper.InitializeAsync(dispatcherQueue);

        _window = new MainWindow(runtime.ViewModel, runtime.ActionDispatcher, runtime.Logger);
        _window.PrepareShellSurface();
        _window.Closed += OnWindowClosed;
        _window.Activate();
        _window.EnsureShellSurfaceZOrder();
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_bootstrapper is not null)
        {
            await _bootstrapper.DisposeAsync();
            _bootstrapper = null;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _bootstrapper?.Logger.Error("Unhandled exception reached App.", e.Exception);
    }
}
