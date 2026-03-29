namespace FlowShellBar.App.Application.Models;

public enum BarLiveSourceState
{
    Disconnected = 0,
    Connected = 1,
}

public enum BarFallbackState
{
    Disabled = 0,
    Enabled = 1,
}

public sealed record BarProjectionSourcesModel(
    BarLiveSourceState FlowtileWm,
    BarLiveSourceState FlowShellCore,
    BarFallbackState Fallback);
