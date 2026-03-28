namespace FlowShellBar.App.Application.Models;

public sealed record StatusClusterModel(
    bool IsNetworkAvailable,
    bool IsAudioAvailable,
    bool HasNotifications);
