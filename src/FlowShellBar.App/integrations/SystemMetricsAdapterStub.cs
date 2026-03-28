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
}
