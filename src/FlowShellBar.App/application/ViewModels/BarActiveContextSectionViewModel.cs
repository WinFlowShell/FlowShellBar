namespace FlowShellBar.App.Application.ViewModels;

public sealed class BarActiveContextSectionViewModel : BindableBase
{
    private string _activeWindowAppName = string.Empty;
    private string _activeWindowTitle = string.Empty;
    private string _activeWorkspaceLabel = string.Empty;

    public string ActiveWindowAppName
    {
        get => _activeWindowAppName;
        set => SetProperty(ref _activeWindowAppName, value);
    }

    public string ActiveWindowTitle
    {
        get => _activeWindowTitle;
        set => SetProperty(ref _activeWindowTitle, value);
    }

    public string ActiveWorkspaceLabel
    {
        get => _activeWorkspaceLabel;
        set => SetProperty(ref _activeWorkspaceLabel, value);
    }
}
