namespace FlowShellBar.App.Application.ViewModels;

public sealed class BarResourcePopupSectionViewModel : BindableBase
{
    private string _ramUsedText = string.Empty;
    private string _ramFreeText = string.Empty;
    private string _ramTotalText = string.Empty;
    private string _cpuTemperatureText = string.Empty;
    private string _gpuTemperatureText = string.Empty;
    private string _cpuLoadText = string.Empty;
    private string _gpuLoadText = string.Empty;

    public string RamUsedText
    {
        get => _ramUsedText;
        set => SetProperty(ref _ramUsedText, value);
    }

    public string RamFreeText
    {
        get => _ramFreeText;
        set => SetProperty(ref _ramFreeText, value);
    }

    public string RamTotalText
    {
        get => _ramTotalText;
        set => SetProperty(ref _ramTotalText, value);
    }

    public string CpuTemperatureText
    {
        get => _cpuTemperatureText;
        set => SetProperty(ref _cpuTemperatureText, value);
    }

    public string GpuTemperatureText
    {
        get => _gpuTemperatureText;
        set => SetProperty(ref _gpuTemperatureText, value);
    }

    public string CpuLoadText
    {
        get => _cpuLoadText;
        set => SetProperty(ref _cpuLoadText, value);
    }

    public string GpuLoadText
    {
        get => _gpuLoadText;
        set => SetProperty(ref _gpuLoadText, value);
    }
}
