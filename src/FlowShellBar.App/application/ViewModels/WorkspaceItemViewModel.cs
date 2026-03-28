namespace FlowShellBar.App.Application.ViewModels;

public sealed class WorkspaceItemViewModel : BindableBase
{
    private int _id;
    private bool _isActive;
    private bool _isOccupied;
    private string _label = string.Empty;

    public int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsOccupied
    {
        get => _isOccupied;
        set => SetProperty(ref _isOccupied, value);
    }
}
