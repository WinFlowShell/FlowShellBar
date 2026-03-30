using FlowShellBar.App.Diagnostics;

namespace FlowShellBar.App.Integrations;

public sealed class SystemMetricsAdapterStub : ISystemMetricsAdapter
{
    public SystemMetricsAdapterStub(IAppLogger logger)
    {
        logger.Info("SystemMetrics adapter stub initialized.");
    }

    public SystemMetricsSnapshot ReadSnapshot()
    {
        return new SystemMetricsSnapshot(
            IsNetworkAvailable: true,
            IsAudioAvailable: true,
            MemoryUsagePercent: 47,
            MemoryUsedBytes: 9UL * 1024 * 1024 * 1024,
            MemoryAvailableBytes: 10UL * 1024 * 1024 * 1024,
            MemoryTotalBytes: 19UL * 1024 * 1024 * 1024,
            CpuUsagePercent: 44,
            GpuUsagePercent: 12,
            CpuTemperatureCelsius: 58,
            GpuTemperatureCelsius: 51);
    }
}
