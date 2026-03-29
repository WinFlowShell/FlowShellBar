using FlowShellBar.App.Diagnostics;

namespace FlowShellBar.App.Integrations;

public sealed class SystemMetricsAdapterStub : ISystemMetricsAdapter
{
    public SystemMetricsAdapterStub(IAppLogger logger)
    {
        logger.Info("SystemMetrics adapter stub initialized.");
    }

    public bool IsNetworkAvailable => true;

    public bool IsAudioAvailable => true;

    public int MemoryUsagePercent => 47;

    public int CpuUsagePercent => 44;

    public int TemperatureCelsius => 3;
}
