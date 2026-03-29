namespace FlowShellBar.App.Integrations;

public enum FlowShellCoreSessionMode
{
    Unknown = 0,
    Normal = 1,
    Degraded = 2,
    SafeMode = 3,
    ShuttingDown = 4,
}

public sealed record FlowShellCoreSessionProjection(
    FlowShellCoreSessionMode SessionMode,
    bool IsBarRegistered,
    bool IsBarReady,
    string BarStatus);
