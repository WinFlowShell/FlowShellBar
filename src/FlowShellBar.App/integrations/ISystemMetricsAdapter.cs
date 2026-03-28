namespace FlowShellBar.App.Integrations;

public interface ISystemMetricsAdapter
{
    bool IsNetworkAvailable { get; }

    bool IsAudioAvailable { get; }
}
