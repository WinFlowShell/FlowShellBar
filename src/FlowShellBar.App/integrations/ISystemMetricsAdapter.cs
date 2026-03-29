namespace FlowShellBar.App.Integrations;

public interface ISystemMetricsAdapter
{
    bool IsNetworkAvailable { get; }

    bool IsAudioAvailable { get; }

    int MemoryUsagePercent { get; }

    int CpuUsagePercent { get; }

    int TemperatureCelsius { get; }
}
