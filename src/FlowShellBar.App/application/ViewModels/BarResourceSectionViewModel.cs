namespace FlowShellBar.App.Application.ViewModels;

public sealed class BarResourceSectionViewModel
{
    public BarResourceIndicatorViewModel Memory { get; } = new();

    public BarResourceIndicatorViewModel Temperature { get; } = new();

    public BarResourceIndicatorViewModel Cpu { get; } = new();

    public BarResourcePopupSectionViewModel Popup { get; } = new();
}
