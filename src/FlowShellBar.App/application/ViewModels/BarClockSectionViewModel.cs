namespace FlowShellBar.App.Application.ViewModels;

public sealed class BarClockSectionViewModel : BindableBase
{
    private string _currentTime = string.Empty;
    private string _currentDate = string.Empty;

    public string CurrentTime
    {
        get => _currentTime;
        set => SetProperty(ref _currentTime, value);
    }

    public string CurrentDate
    {
        get => _currentDate;
        set => SetProperty(ref _currentDate, value);
    }
}
