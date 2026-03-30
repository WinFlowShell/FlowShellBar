namespace FlowShellBar.App.Integrations;

public interface ISystemMetricsAdapter
{
    SystemMetricsSnapshot ReadSnapshot();
}
